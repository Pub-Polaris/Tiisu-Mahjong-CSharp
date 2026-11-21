using System.Net;
using System.Net.Sockets;

namespace Tiisu.Net;

/// <summary>房主端 TCP 服务：接收客户端操作、发送各自可见的脱敏快照。</summary>
public sealed class PeerServer : IAsyncDisposable
{
    private readonly RoomInfo _room;
    private readonly int _port;
    private readonly TcpListener _listener;
    private readonly LanDiscovery _discovery = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<PeerConnection> _connections = new();
    private readonly object _lock = new();
    private LanDiscovery.LanAdvertiser? _advertiser;
    private Task? _acceptTask;
    private bool _started;

    /// <summary>Host 自己占 Seat 0；远程客户端从 Seat 1 开始。</summary>
    public const int HostSeat = 0;

    public PeerServer(RoomInfo room, int port = NetworkConstants.DefaultPort)
    {
        _room = room;
        _port = port;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public RoomInfo Room => CurrentRoom();

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
        if (_started)
        {
            return;
        }

        _started = true;
        _listener.Start();
        _advertiser = _discovery.Advertise(_room, CurrentRoom, _port);
        _acceptTask = AcceptLoopAsync();
    }

    private RoomInfo CurrentRoom() =>
        _room with
        {
            Players = 1 + _connections.Count(c => c.Seat > HostSeat),
            State = "lobby",
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

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var connection = new PeerConnection(client);
        try
        {
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
            lock (_lock)
            {
                _connections.Remove(connection);
            }

            connection.Channel.Dispose();
            BroadcastLobby();
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

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        _advertiser?.Dispose();
        _discovery.Dispose();
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
        }

        _cts.Dispose();
    }

    internal sealed class PeerConnection
    {
        public PeerConnection(TcpClient client)
        {
            Client = client;
            Channel = new JsonLineChannel(client.GetStream());
        }

        public TcpClient Client { get; }
        public JsonLineChannel Channel { get; }
        public int Seat { get; set; } = -1;
        public ClientHello? Hello { get; set; }
        public bool Ready { get; set; }
    }
}
