using System.Net.Sockets;
using System.Net.WebSockets;

namespace Tiisu.Net;

/// <summary>
/// 客户端：只维护欢迎信息、大厅和脱敏快照，不引用 Tiisu.Engine，
/// 不计算发牌/番符；所有操作都以 <see cref="CommandRequest"/> 发给 Host。
/// 支持 TCP 行协议和 WebSocket，协议由房间的 transport 决定。
/// </summary>
public sealed class PeerClient : IAsyncDisposable
{
    private readonly IPeerChannel _channel;
    private readonly TcpClient? _tcp;
    private readonly ClientWebSocket? _webSocket;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    private bool _started;
    private bool _disposed;

    private PeerClient(TcpClient? tcp, ClientWebSocket? webSocket, IPeerChannel channel)
    {
        _tcp = tcp;
        _webSocket = webSocket;
        _channel = channel;
    }

    public int Seat { get; private set; } = -1;
    public RoomInfo? Room { get; private set; }
    public IReadOnlyList<PlayerLobbyInfo> Players { get; private set; } = Array.Empty<PlayerLobbyInfo>();
    public string? LastError { get; private set; }
    public string Transport => Room?.Transport ?? "tcp";
    public bool IsReady { get; private set; }

    public event Action<ServerWelcome>? Welcome;
    public event Action<IReadOnlyList<PlayerLobbyInfo>>? LobbyChanged;
    public event Action<SnapshotMessage>? SnapshotReceived;
    public event Action<PeerError>? Error;
    public event Action<string>? CheatDetected;
    public event Action<string>? Disconnected;
    public event Action? MatchStarting;
    public event Action<string>? RoundEndJson;
    public event Action<ChatMessage>? ChatReceived;

    public static async Task<PeerClient> ConnectAsync(
        string host,
        string name,
        string version,
        int port = NetworkConstants.DefaultPort,
        string? transport = null,
        CancellationToken cancellationToken = default)
    {
        string selected = RoomInfo.NormalizeTransport(transport);
        PeerClient? client = null;
        try
        {
            if (string.Equals(selected, "websocket", StringComparison.Ordinal))
            {
                var socket = new ClientWebSocket();
                socket.Options.AddSubProtocol("tiisu-peer");
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await socket.ConnectAsync(new Uri($"ws://{host}:{port}/tiisu"), cancellationToken)
                    .ConfigureAwait(false);
                client = new PeerClient(null, socket, new WebSocketPeerChannel(socket));
            }
            else
            {
                var tcp = new TcpClient(AddressFamily.InterNetwork);
                tcp.NoDelay = true;
                await tcp.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                client = new PeerClient(tcp, null, new TcpJsonLineChannel(tcp.GetStream()));
            }

            await client.StartAsync(name, version, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task StartAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        await _channel.SendAsync(PeerMessageTypes.Hello,
            new ClientHello(name, version), cancellationToken: cancellationToken).ConfigureAwait(false);

        PeerEnvelope? response = await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (response is null || response.Type != PeerMessageTypes.Welcome)
        {
            throw new InvalidDataException("host did not accept the handshake");
        }

        ServerWelcome? welcome = response.GetPayload<ServerWelcome>();
        if (welcome is null)
        {
            throw new InvalidDataException("invalid welcome payload");
        }

        Seat = welcome.Seat;
        Room = welcome.Room;
        Players = welcome.Players;
        Welcome?.Invoke(welcome);
        _receiveTask = ReceiveLoopAsync();
    }

    public Task SendCommandAsync(string kind, long revision, int index = -1, int option = 0,
        string? text = null, string? snapshotHash = null, CancellationToken cancellationToken = default)
    {
        var command = new CommandRequest(Seat, revision, kind, index, option, text, snapshotHash);
        return _channel.SendAsync(PeerMessageTypes.Command, command, revision, cancellationToken);
    }

    public Task SetReadyAsync(bool ready)
    {
        IsReady = ready;
        return _channel.SendAsync(PeerMessageTypes.Ready, new ReadyRequest(ready));
    }

    public Task SendChatAsync(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (trimmed.Length > NetworkConstants.MaxChatLength)
        {
            trimmed = trimmed[..NetworkConstants.MaxChatLength];
        }

        return _channel.SendAsync(PeerMessageTypes.Chat, new ChatRequest(trimmed));
    }

    public async Task SendByeAsync()
    {
        try
        {
            await _channel.SendAsync(PeerMessageTypes.Bye, new PeerError("bye", "leave")).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                PeerEnvelope? envelope = await _channel.ReceiveAsync(_cts.Token).ConfigureAwait(false);
                if (envelope is null)
                {
                    Disconnected?.Invoke("host closed the connection");
                    return;
                }

                switch (envelope.Type)
                {
                    case PeerMessageTypes.Lobby:
                        Players = envelope.GetPayload<List<PlayerLobbyInfo>>() ?? new List<PlayerLobbyInfo>();
                        IsReady = Players.FirstOrDefault(p => p.Seat == Seat)?.Ready ?? IsReady;
                        LobbyChanged?.Invoke(Players);
                        break;

                    case PeerMessageTypes.Snapshot:
                        var snapshot = envelope.GetPayload<SnapshotMessage>();
                        if (snapshot is not null)
                        {
                            SnapshotReceived?.Invoke(snapshot);
                        }

                        break;

                    case PeerMessageTypes.Error:
                        var error = envelope.GetPayload<PeerError>();
                        if (error is not null)
                        {
                            LastError = error.Message;
                            Error?.Invoke(error);
                        }

                        break;

                    case PeerMessageTypes.CheatDetected:
                        var cheat = envelope.GetPayload<PeerError>();
                        if (cheat is not null)
                        {
                            LastError = cheat.Message;
                            CheatDetected?.Invoke(cheat.Message);
                        }

                        break;

                    case PeerMessageTypes.Bye:
                        Disconnected?.Invoke("host closed the room");
                        return;

                    case "start":
                        MatchStarting?.Invoke();
                        break;

                    case "round_end":
                        RoundEndJson?.Invoke(envelope.Payload.GetRawText());
                        break;

                    case PeerMessageTypes.Chat:
                        ChatMessage? chat = envelope.GetPayload<ChatMessage>();
                        if (chat is not null)
                        {
                            ChatReceived?.Invoke(chat);
                        }

                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            Disconnected?.Invoke("connection lost");
        }
        catch (InvalidDataException)
        {
            Disconnected?.Invoke("invalid server message");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            await SendByeAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        _channel.Dispose();
        _tcp?.Dispose();
        _webSocket?.Dispose();
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _cts.Dispose();
    }
}
