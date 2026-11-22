using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Tiisu.App.Table;
using Tiisu.Engine.Random;
using Tiisu.Net;

namespace Tiisu.App.Runtime;

/// <summary>
/// Host 端引擎封装：本地跑唯一权威 MahjongEngine/MatchRuntime，
/// 远程客户端只通过 <see cref="PeerServer"/> 提交指令、接收按座位脱敏的快照。
/// Client 不实例化本类，也不引用 Tiisu.Engine 的任何状态机/发牌/计分对象。
/// </summary>
public sealed class HostEngine : ITableSession, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);
    private readonly GameSession _local;
    private readonly PeerServer _server;
    private readonly long[] _lastSentRevision = new long[NetworkConstants.MaxPlayers];
    private readonly string?[] _lastSentHash = new string?[NetworkConstants.MaxPlayers];

    /// <summary>
    /// 快照广播串行化：引擎每次状态变化只投递一个信号（容量 1，旧信号被丢弃），
    /// 单个 pump 依次发送，保证同一座位的 revision 单调且不会被旧任务回写。
    /// </summary>
    private readonly Channel<long> _snapshotQueue = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly CancellationTokenSource _pumpCts = new();
    private readonly object _stopLock = new();
    private Task? _pumpTask;
    private string? _lastError;
    private Task? _stopTask;
    private bool _started;
    private bool _disposed;
    private bool _networkSeats;

    public HostEngine(Settings settings, RoomInfo room, int port = NetworkConstants.DefaultPort,
        IRandomSource? random = null, string? transport = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _local = new GameSession(settings, useStores: false, aiStepDelayMilliseconds: 0, random: random);
        _server = new PeerServer(
            room with { Transport = RoomInfo.NormalizeTransport(transport ?? room.Transport) },
            port,
            transport ?? room.Transport);
        _server.CommandReceived += OnCommand;
        _server.CheatDetected += OnCheat;
        _server.ChatReceived += message => ChatReceived?.Invoke(message);
        _server.LobbyChanged += () => LobbyChanged?.Invoke(Lobby);
        _local.UiEvent += OnLocalEvent;
    }

    public Settings Settings { get; }
    public PeerServer Server => _server;
    public int Port => _server.Port;
    public RoomInfo Room => _server.Room;
    public string Transport => _server.Transport;
    public IReadOnlyList<PlayerLobbyInfo> Lobby => _server.Lobby;
    public GameSession LocalSession => _local;

    public TableViewModel LatestSnapshot => _local.LatestSnapshot;
    public MatchPhase Phase => _local.Phase;
    public string? LastError => Volatile.Read(ref _lastError);
    public bool MatchCancelled { get; private set; }

    public event Action<UiEvent>? UiEvent;
    public event Action<IReadOnlyList<PlayerLobbyInfo>>? LobbyChanged;
    public event Action<string>? CheatDetected;
    public event Action<string>? MatchStarted;
    public event Action<ChatMessage>? ChatReceived;

    public void StartServer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _server.ValidateCommand = ValidateCommand;
        _server.Start();
        _ = _server.BroadcastAsync(PeerMessageTypes.Lobby, Lobby);
    }

    /// <summary>
    /// 至少 Host 一人即可开始；未连接的座位由本地 AI 补位。
    /// 远程座位仍然只接受该座位的脱敏快照/指令。
    /// </summary>
    public bool StartMatch(int mode = 1)
    {
        StartServer();
        MatchCancelled = false;
        Volatile.Write(ref _lastError, null);
        if (!Room.AiFill && Lobby.Count < NetworkConstants.MaxPlayers)
        {
            Volatile.Write(ref _lastError, $"该桌未开启 AI 补位，需要 {NetworkConstants.MaxPlayers} 人");
            return false;
        }

        _local.ConfigureNetworkSeats(BuildHumanSeats());
        _networkSeats = true;
        _local.Post(GameCommand.StartMatch(mode));
        _server.State = "playing";
        _ = _server.BroadcastAsync("start", new { mode });
        MatchStarted?.Invoke(Room.RoomName);
        return true;
    }

    private bool[] BuildHumanSeats()
    {
        var seats = new bool[NetworkConstants.MaxPlayers];
        seats[PeerServer.HostSeat] = true;
        foreach (PlayerLobbyInfo player in Lobby)
        {
            if (player.Seat >= 0 && player.Seat < seats.Length)
            {
                seats[player.Seat] = true;
            }
        }

        return seats;
    }

    public void SendChat(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (trimmed.Length > NetworkConstants.MaxChatLength)
        {
            trimmed = trimmed[..NetworkConstants.MaxChatLength];
        }

        _ = _server.BroadcastAsync(PeerMessageTypes.Chat,
            new ChatMessage(PeerServer.HostSeat, Room.HostName, trimmed,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public void Post(GameCommand command)
    {
        if (_disposed)
        {
            return;
        }

        // 联机模式下本机只代表 Seat 0：不是本机行动者的窗口时，忽略本机键鼠命令，
        // 避免房主在自己的界面上替远程玩家出牌 / 和牌（Host 本地命令不经过 ValidateCommand）。
        if (_networkSeats
            && command.Kind is not (GameCommandKind.StartMatch or GameCommandKind.ResetAll
                or GameCommandKind.ApplySettings or GameCommandKind.Resume or GameCommandKind.NextRound)
            && _local.ActiveHumanSeat != PeerServer.HostSeat)
        {
            return;
        }

        if (command.SnapshotRevision == 0)
        {
            command = command with { SnapshotRevision = LatestSnapshot.Revision };
        }

        _local.Post(command);
    }

    private void OnCommand(CommandRequest request)
    {
        if (_disposed || MatchCancelled)
        {
            return;
        }

        if (!TryMap(request, out GameCommand? command))
        {
            Volatile.Write(ref _lastError, "联机指令无法识别");
            return;
        }

        _local.Post(command!);
    }

    private bool ValidateCommand(CommandRequest request)
    {
        if (!_started || request.Seat <= 0 || request.Seat >= NetworkConstants.MaxPlayers)
        {
            return false;
        }

        // 每个客户端只能基于 Host 发给它的最后一份快照操作。
        if (request.Revision != Volatile.Read(ref _lastSentRevision[request.Seat]))
        {
            return false;
        }

        // 客户端声称的快照必须与 Host 实际发给它的脱敏快照一致；
        // 任何“本地算牌/改牌山”的结果都会在这里被发现。
        if (Volatile.Read(ref _lastSentHash[request.Seat]) is not { } expectedHash
            || !string.Equals(request.SnapshotHash, expectedHash, StringComparison.Ordinal))
        {
            return false;
        }

        // 只有当前行动者能操作；防止其它座位越权。
        if (request.Seat != _local.ActiveHumanSeat)
        {
            return false;
        }

        if (!TryMap(request, out GameCommand? command))
        {
            return false;
        }

        if (!IsCommandAllowed(command!, _local.SnapshotForSeat(request.Seat).Actions))
        {
            return false;
        }

        return true;
    }

    private void OnCheat(string reason)
    {
        MatchCancelled = true;
        Volatile.Write(ref _lastError, "发现作弊者，本局取消：" + reason);
        _ = _server.BroadcastAsync(PeerMessageTypes.Error,
            new PeerError("cheat", "发现作弊者，本局取消"));
        _local.Post(GameCommand.ResetAll());
        CheatDetected?.Invoke(reason);
        UiEvent?.Invoke(new ErrorEvent(LastError ?? reason));
    }

    private void OnLocalEvent(UiEvent uiEvent)
    {
        if (_disposed)
        {
            return;
        }

        if (uiEvent is StateChangedEvent)
        {
            // 只发「有新状态」的信号；pump 每次发送时取当前最新快照，
            // 旧信号在容量 1 的队列里被丢弃，不会积压成一串过期快照。
            QueueSnapshotBroadcast();
        }
        else if (uiEvent is RoundEndEvent roundEnd)
        {
            _ = BroadcastRoundEndAsync(roundEnd.Result);
        }
        else if (uiEvent is MatchOverEvent)
        {
            _server.State = "lobby";
        }
        else if (uiEvent is ErrorEvent error)
        {
            Volatile.Write(ref _lastError, error.Message);
        }

        UiEvent?.Invoke(uiEvent);
    }

    private void QueueSnapshotBroadcast()
    {
        _pumpTask ??= Task.Run(SnapshotPumpAsync);
        _snapshotQueue.Writer.TryWrite(0);
    }

    /// <summary>单线程 pump：串行发送各座位快照，容量 1 的队列天然合并过期状态。</summary>
    private async Task SnapshotPumpAsync()
    {
        try
        {
            await foreach (long _ in _snapshotQueue.Reader.ReadAllAsync(_pumpCts.Token).ConfigureAwait(false))
            {
                await BroadcastSnapshotsAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, "快照发送失败: " + ex.Message);
        }
    }

    private async Task BroadcastSnapshotsAsync()
    {
        for (int seat = 1; seat < NetworkConstants.MaxPlayers; seat++)
        {
            TableViewModel snapshot = SnapshotRotation.RotateForViewer(
                _local.SnapshotForSeat(seat), seat) with { CanAdvanceRound = false };
            string json = JsonSerializer.Serialize(snapshot, SnapshotJson);
            string hash = Hash(json);

            // 只有真正写出去才算「已发」：发送失败时保留上一份 revision/hash，
            // 客户端的旧 revision 仍可对上，不会被误判成作弊。
            if (await _server.SendSnapshotAsync(seat, snapshot.Revision, json, _pumpCts.Token)
                    .ConfigureAwait(false))
            {
                Volatile.Write(ref _lastSentRevision[seat], snapshot.Revision);
                Volatile.Write(ref _lastSentHash[seat], hash);
            }
        }
    }

    private static bool TryMap(CommandRequest request, out GameCommand? command)
    {
        command = request.Kind switch
        {
            "discard" => GameCommand.Discard(request.Index),
            "chi" => GameCommand.Chi(request.Option),
            "pon" => GameCommand.Pon(),
            "kan_an" => GameCommand.KanAn(),
            "kan_minkan" => GameCommand.KanMinkan(),
            "kan_kakan" => GameCommand.KanKakan(),
            "tsumo" => GameCommand.Tsumo(),
            "ron" => GameCommand.Ron(),
            "declare_call" => GameCommand.DeclareCall(request.Index),
            "pass" => GameCommand.Pass(),
            "flow" => GameCommand.Flow(request.Text),
            "next_round" => GameCommand.NextRound(),
            _ => null,
        };
        return command is not null;
    }

    private static bool IsCommandAllowed(GameCommand command, HumanAction actions) => command.Kind switch
    {
        GameCommandKind.Discard => (actions & HumanAction.Discard) != 0,

        // 报听是独立动作：荣和 / 副露窗口里只有 DeclareCall 位，没有 Discard 位。
        GameCommandKind.DeclareCall => (actions & HumanAction.DeclareCall) != 0,
        GameCommandKind.Tsumo => (actions & HumanAction.Tsumo) != 0,
        GameCommandKind.Ron => (actions & HumanAction.Ron) != 0,
        GameCommandKind.Pon => (actions & HumanAction.Pon) != 0,
        GameCommandKind.Chi => (actions & HumanAction.Chi) != 0,
        GameCommandKind.KanAn or GameCommandKind.KanMinkan or GameCommandKind.KanKakan =>
            (actions & HumanAction.Kan) != 0,
        GameCommandKind.Pass => (actions & HumanAction.Pass) != 0,
        GameCommandKind.Flow => (actions & HumanAction.Flow) != 0,
        GameCommandKind.NextRound => true,
        _ => false,
    };

    private async Task BroadcastRoundEndAsync(RoundResult result)
    {
        try
        {
            await _server.BroadcastAsync("round_end", result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, "结算广播失败: " + ex.Message);
        }
    }

    private static string Hash(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    /// <summary>幂等停止：先停广播 pump，再停止监听/广告，最后结束本地引擎线程。</summary>
    public Task StopAsync()
    {
        lock (_stopLock)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        _disposed = true;
        _local.UiEvent -= OnLocalEvent;
        _server.CommandReceived -= OnCommand;
        _server.CheatDetected -= OnCheat;

        _snapshotQueue.Writer.TryComplete();
        _pumpCts.Cancel();
        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch
            {
                // pump 已在内部吞掉取消/发送异常。
            }
        }

        try
        {
            await _server.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _pumpCts.Dispose();
            _local.Dispose();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    public void Dispose()
    {
        try
        {
            if (!StopAsync().Wait(TimeSpan.FromSeconds(5)))
            {
                Volatile.Write(ref _lastError, "关闭联机房间超时");
            }
        }
        catch (AggregateException ex)
        {
            Volatile.Write(ref _lastError, ex.GetBaseException().Message);
        }
    }
}
