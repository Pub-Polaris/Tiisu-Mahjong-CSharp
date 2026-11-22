using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tiisu.Net;

/// <summary>线协议消息类型。客户端永远只收快照/只发指令。</summary>
public static class PeerMessageTypes
{
    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Lobby = "lobby";
    public const string Command = "command";
    public const string Snapshot = "snapshot";
    public const string Error = "error";
    public const string CheatDetected = "cheat";
    public const string Bye = "bye";
    public const string Ready = "ready";
    public const string Chat = "chat";
}

public sealed record PlayerLobbyInfo(int Seat, string Name, bool Ready);

public sealed record ClientHello(string Name, string Version, string? RoomToken = null);

public sealed record ServerWelcome(
    int Seat,
    RoomInfo Room,
    IReadOnlyList<PlayerLobbyInfo> Players,
    string Protocol);

public sealed record CommandRequest(
    int Seat,
    long Revision,
    string Kind,
    int Index = -1,
    int Option = 0,
    string? Text = null,
    string? SnapshotHash = null);

/// <summary>
/// 脱敏快照。Host 只填当前接收者可见的内容；客户端不持有牌山/引擎。
/// Snapshot 用 JSON 字符串承载，避免客户端引用 Tiisu.Engine/App 类型。
/// </summary>
public sealed record SnapshotMessage(long Revision, int Seat, string Snapshot);

public sealed record PeerError(string Code, string Message);

public sealed record ReadyRequest(bool Ready);

public sealed record ChatRequest(string Text);

public sealed record ChatMessage(int Seat, string From, string Text, long Timestamp);

public sealed record PeerEnvelope(
    string Protocol,
    string Type,
    JsonElement Payload,
    long Sequence = 0)
{
    public static PeerEnvelope Create<T>(string type, T payload, long sequence = 0)
    {
        JsonElement element = JsonSerializer.SerializeToElement(payload, PeerJson.Options);
        return new PeerEnvelope(NetworkConstants.Protocol, type, element, sequence);
    }

    public T? GetPayload<T>() => Payload.Deserialize<T>(PeerJson.Options);
}

public static class PeerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>一条联机连接上的 JSON 收发通道；TCP 行协议和 WebSocket 都实现它。</summary>
internal interface IPeerChannel : IDisposable
{
    Task SendAsync<T>(string type, T payload, long sequence = 0,
        CancellationToken cancellationToken = default);

    Task<PeerEnvelope?> ReceiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>一条 TCP 连接上的行分隔 JSON 收发器。</summary>
internal sealed class TcpJsonLineChannel : IPeerChannel
{
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly StringBuilder _lineBuffer = new();
    private readonly char[] _readChars = new char[8 * 1024];

    public TcpJsonLineChannel(Stream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream, System.Text.Encoding.UTF8, false, 16 * 1024, leaveOpen: true);
        _writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 16 * 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
    }

    public async Task SendAsync<T>(string type, T payload, long sequence = 0,
        CancellationToken cancellationToken = default)
    {
        PeerEnvelope envelope = PeerEnvelope.Create(type, payload, sequence);
        string line = JsonSerializer.Serialize(envelope, PeerJson.Options);
        if (line.Length > NetworkConstants.MaxMessageBytes)
        {
            throw new InvalidOperationException("outgoing message too large");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PeerEnvelope?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        string? line = await ReadLineBoundedAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        if (line.Length is 0 or > NetworkConstants.MaxMessageBytes)
        {
            throw new InvalidDataException("invalid message length");
        }

        PeerEnvelope? envelope = JsonSerializer.Deserialize<PeerEnvelope>(line, PeerJson.Options);
        if (envelope is null
            || !string.Equals(envelope.Protocol, NetworkConstants.Protocol, StringComparison.Ordinal))
        {
            throw new InvalidDataException("invalid peer protocol");
        }

        return envelope;
    }

    /// <summary>
    /// 按行读取，但在读完一整行之前就限制缓冲区大小：超大无换行的输入会在
    /// <see cref="NetworkConstants.MaxMessageBytes"/> 处直接失败，不会先把内存吃满。
    /// </summary>
    private async Task<string?> ReadLineBoundedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            for (int i = 0; i < _lineBuffer.Length; i++)
            {
                if (_lineBuffer[i] != '\n')
                {
                    continue;
                }

                string line = _lineBuffer.ToString(0, i);
                _lineBuffer.Remove(0, i + 1);
                if (line.EndsWith('\r'))
                {
                    line = line[..^1];
                }

                return line;
            }

            if (_lineBuffer.Length > NetworkConstants.MaxMessageBytes)
            {
                throw new InvalidDataException("incoming message too large");
            }

            int read = await _reader.ReadAsync(_readChars.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_lineBuffer.Length == 0)
                {
                    return null;
                }

                throw new InvalidDataException("truncated message");
            }

            _lineBuffer.Append(_readChars, 0, read);
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _stream.Dispose();
        _writeLock.Dispose();
    }
}

/// <summary>WebSocket text-frame JSON 通道。</summary>
internal sealed class WebSocketPeerChannel : IPeerChannel
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public WebSocketPeerChannel(WebSocket socket)
    {
        _socket = socket;
    }

    public async Task SendAsync<T>(string type, T payload, long sequence = 0,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _socket.State != WebSocketState.Open)
        {
            throw new IOException("websocket is closed");
        }

        PeerEnvelope envelope = PeerEnvelope.Create(type, payload, sequence);
        string json = JsonSerializer.Serialize(envelope, PeerJson.Options);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > NetworkConstants.MaxMessageBytes)
        {
            throw new InvalidOperationException("outgoing message too large");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PeerEnvelope?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        using var payload = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                throw new InvalidDataException("binary messages are not supported");
            }

            payload.Write(buffer, 0, result.Count);
            if (payload.Length > NetworkConstants.MaxMessageBytes)
            {
                throw new InvalidDataException("incoming message too large");
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            if (payload.Length == 0)
            {
                // 合法的空文本帧（某些实现的 keepalive）：跳过继续读，不能当断线。
                continue;
            }

            string json = Encoding.UTF8.GetString(payload.GetBuffer(), 0, (int)payload.Length);
            PeerEnvelope? envelope = JsonSerializer.Deserialize<PeerEnvelope>(json, PeerJson.Options);
            if (envelope is null
                || !string.Equals(envelope.Protocol, NetworkConstants.Protocol, StringComparison.Ordinal))
            {
                throw new InvalidDataException("invalid peer protocol");
            }

            return envelope;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();
        _socket.Dispose();
    }
}
