using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tiisu.App.Table;
using Tiisu.Engine.Random;
using Tiisu.Net;

namespace Tiisu.App.Runtime;

/// <summary>
/// Host 端引擎封装：本地跑唯一权威 MahjongEngine/MatchRuntime，
/// 远程客户端只通过 <see cref="PeerServer"/> 提交指令、接收按座位脱敏的快照。
/// Client 不实例化本类，也不引用 Tiisu.Engine 的任何状态机/发牌/计分对象。
/// </summary>
public sealed class HostEngine : ITableSession
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);
    private readonly GameSession _local;
    private readonly PeerServer _server;
    private readonly long[] _lastSentRevision = new long[NetworkConstants.MaxPlayers];
    private readonly string?[] _lastSentHash = new string?[NetworkConstants.MaxPlayers];
    private string? _lastError;
    private bool _started;
    private bool _disposed;

    public HostEngine(Settings settings, RoomInfo room, int port = NetworkConstants.DefaultPort,
        IRandomSource? random = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _local = new GameSession(settings, useStores: false, aiStepDelayMilliseconds: 0, random: random);
        _server = new PeerServer(room, port);
        _server.CommandReceived += OnCommand;
        _server.CheatDetected += OnCheat;
        _server.LobbyChanged += () => LobbyChanged?.Invoke(Lobby);
        _local.UiEvent += OnLocalEvent;
    }

    public Settings Settings { get; }
    public PeerServer Server => _server;
    public int Port => _server.Port;
    public RoomInfo Room => _server.Room;
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

    public void StartServer()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _server.ValidateCommand = ValidateCommand;
        _server.Start();
        _ = _server.BroadcastAsync(PeerMessageTypes.Lobby, Lobby);
    }

    /// <summary>Host 在四家到齐后从菜单/房间页调用。</summary>
    public void StartMatch(int mode = 1)
    {
        StartServer();
        MatchCancelled = false;
        Volatile.Write(ref _lastError, null);
        if (Lobby.Count < NetworkConstants.MaxPlayers)
        {
            Volatile.Write(ref _lastError, $"需要 {NetworkConstants.MaxPlayers} 人，当前 {Lobby.Count} 人");
            return;
        }

        _local.ConfigureNetworkSeats(new[] { true, true, true, true });
        _local.Post(GameCommand.StartMatch(mode));
        _server.State = "playing";
        _ = _server.BroadcastAsync("start", new { mode });
        MatchStarted?.Invoke(Room.RoomName);
    }

    public void Post(GameCommand command)
    {
        if (_disposed)
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
        if (request.Revision != _lastSentRevision[request.Seat])
        {
            return false;
        }

        // 客户端声称的快照必须与 Host 实际发给它的脱敏快照一致；
        // 任何“本地算牌/改牌山”的结果都会在这里被发现。
        if (_lastSentHash[request.Seat] is not { } expectedHash
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
        if (uiEvent is StateChangedEvent changed)
        {
            _ = BroadcastSnapshotsAsync(changed.Snapshot.Revision);
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

    private async Task BroadcastSnapshotsAsync(long revision)
    {
        try
        {
            for (int seat = 1; seat < NetworkConstants.MaxPlayers; seat++)
            {
                TableViewModel snapshot = _local.SnapshotForSeat(seat) with { Revision = revision };
                _lastSentRevision[seat] = revision;
                string json = JsonSerializer.Serialize(snapshot, SnapshotJson);
                _lastSentHash[seat] = Hash(json);
                await _server.SendSnapshotAsync(seat, revision, json).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, "快照发送失败: " + ex.Message);
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
        GameCommandKind.Discard or GameCommandKind.DeclareCall => (actions & HumanAction.Discard) != 0,
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _local.UiEvent -= OnLocalEvent;
        _server.CommandReceived -= OnCommand;
        _server.CheatDetected -= OnCheat;
        _local.Dispose();
        _ = _server.DisposeAsync();
    }
}
