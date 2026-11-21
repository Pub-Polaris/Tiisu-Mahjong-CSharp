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
    string? Text = null);

/// <summary>
/// 脱敏快照。Host 只填当前接收者可见的内容；客户端不持有牌山/引擎。
/// Snapshot 用 JSON 字符串承载，避免客户端引用 Tiisu.Engine/App 类型。
/// </summary>
public sealed record SnapshotMessage(long Revision, int Seat, string Snapshot);

public sealed record PeerError(string Code, string Message);

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

/// <summary>一条 TCP 连接上的行分隔 JSON 收发器。</summary>
internal sealed class JsonLineChannel : IDisposable
{
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonLineChannel(Stream stream)
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
        string? line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
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

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _stream.Dispose();
        _writeLock.Dispose();
    }
}
