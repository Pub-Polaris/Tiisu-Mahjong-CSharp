using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace Tiisu.Net;

/// <summary>房主端 TCP 服务：接收客户端操作、发送各自可见的脱敏快照。</summary>
public sealed class PeerServer : IAsyncDisposable
{
    private readonly RoomInfo _room;
    private readonly int _port;
    private readonly string _transport;
    private readonly TcpListener _listener;
    private readonly LanDiscovery _discovery = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<PeerConnection> _connections = new();
    private readonly List<Task> _clientTasks = new();
    private readonly object _lock = new();
    private LanDiscovery.LanAdvertiser? _advertiser;
    private Task? _acceptTask;
    private Task? _stopTask;
    private bool _started;
    private bool _disposed;

    /// <summary>Host 自己占 Seat 0；远程客户端从 Seat 1 开始。</summary>
    public const int HostSeat = 0;

    public PeerServer(RoomInfo room, int port = NetworkConstants.DefaultPort, string? transport = null)
    {
        _room = room;
        _port = port;
        _transport = RoomInfo.NormalizeTransport(transport ?? room.Transport);
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public string Transport => _transport;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public RoomInfo Room => CurrentRoom();

    /// <summary>对外展示的房间状态：lobby / playing。playing 时客户端不应再加入。</summary>
    public string State { get; set; } = "lobby";

    public IReadOnlyList<PlayerLobbyInfo> Lobby
    {
        get
        {
            lock (_lock)
            {
                var result = new List<PlayerLobbyInfo>
                {
                    new(HostSeat, _room.HostName, true),
                };
                result.AddRange(_connections
                    .Where(c => c.Seat > HostSeat && c.Hello is not null)
                    .OrderBy(c => c.Seat)
                    .Select(c => new PlayerLobbyInfo(c.Seat, c.Hello!.Name, c.Ready)));
                return result;
            }
        }
    }

    /// <summary>Host 校验操作是否与权威牌山/手牌/巡目一致；返回 false 会立即取消本局。</summary>
    public Func<CommandRequest, bool>? ValidateCommand { get; set; }

    /// <summary>收到一条通过校验的客户端操作。</summary>
    public event Action<CommandRequest>? CommandReceived;

    /// <summary>发现不一致的操作/版本；Host 应终止本局并提示。</summary>
    public event Action<string>? CheatDetected;

    public event Action? LobbyChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        try
        {
            _started = true;
            _listener.Start();
            _advertiser = _discovery.Advertise(_room, CurrentRoom, _port);
            _acceptTask = AcceptLoopAsync();
        }
        catch
        {
            _started = false;
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }

            _discovery.Dispose();
            throw;
        }
    }

    private RoomInfo CurrentRoom() =>
        _room with
        {
            Players = 1 + _connections.Count(c => c.Seat > HostSeat),
            State = State,
            Transport = _transport,
        };

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            TrackClient(HandleClientAsync(client));
        }
    }

    private void TrackClient(Task task)
    {
        lock (_lock)
        {
            _clientTasks.Add(task);
        }

        _ = task.ContinueWith(completed =>
        {
            lock (_lock)
            {
                _clientTasks.Remove(completed);
            }
        }, TaskScheduler.Default);
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        PeerConnection? connection = null;
        try
        {
            IPeerChannel channel;
            if (string.Equals(_transport, "websocket", StringComparison.OrdinalIgnoreCase))
            {
                WebSocket? socket = await WebSocketUpgrade.AcceptAsync(client, _cts.Token)
                    .ConfigureAwait(false);
                if (socket is null)
                {
                    return;
                }

                channel = new WebSocketPeerChannel(socket);
            }
            else
            {
                channel = new TcpJsonLineChannel(client.GetStream());
            }

            connection = new PeerConnection(client, channel);
            PeerEnvelope? helloEnvelope = await connection.Channel.ReceiveAsync(_cts.Token).ConfigureAwait(false);
            ClientHello? hello = helloEnvelope?.GetPayload<ClientHello>();
            if (hello is null || helloEnvelope?.Type != PeerMessageTypes.Hello)
            {
                await connection.Channel.SendAsync(PeerMessageTypes.Error,
                    new PeerError("handshake", "expected hello"), cancellationToken: _cts.Token).ConfigureAwait(false);
                return;
            }

            int seat;
            lock (_lock)
            {
                if (_connections.Count >= NetworkConstants.MaxPlayers - 1)
                {
                    _ = connection.Channel.SendAsync(PeerMessageTypes.Error,
                        new PeerError("full", "room is full"), cancellationToken: _cts.Token);
                    return;
                }

                seat = Enumerable.Range(1, NetworkConstants.MaxPlayers - 1)
                    .First(s => _connections.All(c => c.Seat != s));
                connection.Seat = seat;
                connection.Hello = hello;
                _connections.Add(connection);
            }

            await connection.Channel.SendAsync(PeerMessageTypes.Welcome,
                new ServerWelcome(seat, CurrentRoom(), Lobby, NetworkConstants.Protocol),
                cancellationToken: _cts.Token).ConfigureAwait(false);
            BroadcastLobby();

            while (!_cts.IsCancellationRequested)
            {
                PeerEnvelope? envelope = await connection.Channel.ReceiveAsync(_cts.Token).ConfigureAwait(false);
                if (envelope is null)
                {
                    break;
                }

                if (envelope.Type == PeerMessageTypes.Command)
                {
                    CommandRequest? command = envelope.GetPayload<CommandRequest>();
                    if (command is null || command.Seat != seat)
                    {
                        await RejectAsync(seat, "seat mismatch").ConfigureAwait(false);
                        return;
                    }

                    if (ValidateCommand is not null && !ValidateCommand(command))
                    {
                        await RejectAsync(seat, "command inconsistent with authoritative wall/state")
                            .ConfigureAwait(false);
                        return;
                    }

                    CommandReceived?.Invoke(command);
                }
                else if (envelope.Type == PeerMessageTypes.Bye)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidDataException)
        {
        }
        finally
        {
            if (connection is not null)
            {
                lock (_lock)
                {
                    _connections.Remove(connection);
                }

                connection.Channel.Dispose();
                connection.Client.Dispose();
                BroadcastLobby();
            }
            else
            {
                client.Dispose();
            }
        }
    }

    private async Task RejectAsync(int seat, string reason)
    {
        CheatDetected?.Invoke(reason);
        foreach (var connection in SnapshotConnections())
        {
            try
            {
                await connection.Channel.SendAsync(PeerMessageTypes.CheatDetected,
                    new PeerError("cheat", reason), cancellationToken: _cts.Token).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public async Task SendSnapshotAsync(int seat, long revision, string snapshotJson,
        CancellationToken cancellationToken = default)
    {
        PeerConnection? connection = SnapshotConnections().FirstOrDefault(c => c.Seat == seat);
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.Channel.SendAsync(PeerMessageTypes.Snapshot,
                new SnapshotMessage(revision, seat, snapshotJson),
                revision, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public async Task BroadcastAsync<T>(string type, T payload, CancellationToken cancellationToken = default)
    {
        foreach (var connection in SnapshotConnections())
        {
            try
            {
                await connection.Channel.SendAsync(type, payload, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }
    }

    private void BroadcastLobby()
    {
        LobbyChanged?.Invoke();
        _ = BroadcastAsync(PeerMessageTypes.Lobby, Lobby);
    }

    private PeerConnection[] SnapshotConnections()
    {
        lock (_lock)
        {
            return _connections.ToArray();
        }
    }

    public Task StopAsync()
    {
        if (_stopTask is not null)
        {
            return _stopTask;
        }

        _disposed = true;
        _stopTask = StopCoreAsync();
        return _stopTask;
    }

    private async Task StopCoreAsync()
    {
        State = "closed";
        _cts.Cancel();

        // Stop discovery first so no new clients can find this room.
        _advertiser?.Dispose();
        _advertiser = null;
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
        }

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        PeerConnection[] connections = SnapshotConnections();
        foreach (var connection in connections)
        {
            try
            {
                await connection.Channel.SendAsync(PeerMessageTypes.Bye,
                    new PeerError("closed", "host closed")).ConfigureAwait(false);
            }
            catch
            {
            }

            connection.Channel.Dispose();
            connection.Client.Dispose();
        }

        Task[] clientTasks;
        lock (_lock)
        {
            clientTasks = _clientTasks.ToArray();
            _clientTasks.Clear();
            _connections.Clear();
        }

        try
        {
            await Task.WhenAll(clientTasks).ConfigureAwait(false);
        }
        catch
        {
            // Client tasks are allowed to end by cancellation during shutdown.
        }

        _discovery.Dispose();
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    internal sealed class PeerConnection
    {
        public PeerConnection(TcpClient client, IPeerChannel channel)
        {
            Client = client;
            Channel = channel;
        }

        public TcpClient Client { get; }
        public IPeerChannel Channel { get; }
        public int Seat { get; set; } = -1;
        public ClientHello? Hello { get; set; }
        public bool Ready { get; set; }
    }
}
