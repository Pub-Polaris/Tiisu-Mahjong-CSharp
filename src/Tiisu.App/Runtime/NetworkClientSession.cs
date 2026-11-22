using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tiisu.App.Table;
using Tiisu.Net;

namespace Tiisu.App.Runtime;

/// <summary>
/// 联机客户端会话：只消费 Host 发来的脱敏 <see cref="TableViewModel"/> 快照，
/// 只把 <see cref="GameCommand"/> 转成网络指令。这个类不创建 MahjongEngine、
/// 不碰牌山/发牌/判役/计分。
/// </summary>
public sealed class NetworkClientSession : ITableSession
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly PeerClient _peer;
    private readonly SynchronizationContext? _uiContext;
    private TableViewModel _latest;
    private string? _lastError;
    private string? _snapshotHash;
    private bool _disposed;

    public NetworkClientSession(PeerClient peer, SynchronizationContext? uiContext = null)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _uiContext = uiContext ?? SynchronizationContext.Current;
        _latest = EmptySnapshot(peer.Seat);
        _peer.SnapshotReceived += OnSnapshot;
        _peer.Error += OnError;
        _peer.CheatDetected += OnCheat;
    }

    public int Seat => _peer.Seat;

    public TableViewModel LatestSnapshot => Volatile.Read(ref _latest);

    public MatchPhase Phase => ParsePhase(LatestSnapshot.Phase);

    public string? LastError => Volatile.Read(ref _lastError);

    public event Action<UiEvent>? UiEvent;

    public void Post(GameCommand command)
    {
        if (_disposed || command is null)
        {
            return;
        }

        if (command.Kind is GameCommandKind.ApplySettings
            or GameCommandKind.Resume
            or GameCommandKind.ResetAll)
        {
            Dispatch(new ErrorEvent("联机模式下由房主控制设置/重置"));
            return;
        }

        string kind = command.Kind switch
        {
            GameCommandKind.StartMatch => "start",
            GameCommandKind.Discard => "discard",
            GameCommandKind.Chi => "chi",
            GameCommandKind.Pon => "pon",
            GameCommandKind.KanAn => "kan_an",
            GameCommandKind.KanMinkan => "kan_minkan",
            GameCommandKind.KanKakan => "kan_kakan",
            GameCommandKind.Tsumo => "tsumo",
            GameCommandKind.Ron => "ron",
            GameCommandKind.DeclareCall => "declare_call",
            GameCommandKind.Pass => "pass",
            GameCommandKind.Flow => "flow",
            GameCommandKind.NextRound => "next_round",
            _ => command.Kind.ToString().ToLowerInvariant(),
        };

        long revision = command.SnapshotRevision != 0
            ? command.SnapshotRevision
            : LatestSnapshot.Revision;
        _ = _peer.SendCommandAsync(kind, revision, command.Index, command.Option, command.Text, _snapshotHash)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Dispatch(new ErrorEvent(task.Exception?.GetBaseException().Message ?? "send failed"));
                }
            }, TaskScheduler.Default);
    }

    private void OnSnapshot(SnapshotMessage message)
    {
        if (message.Seat != Seat)
        {
            return;
        }

        try
        {
            TableViewModel? snapshot = JsonSerializer.Deserialize<TableViewModel>(message.Snapshot, SnapshotJson);
            if (snapshot is null)
            {
                return;
            }

            _snapshotHash = Hash(message.Snapshot);
            snapshot = snapshot with { Revision = message.Revision };
            Volatile.Write(ref _latest, snapshot);
            Dispatch(new StateChangedEvent(snapshot));
        }
        catch (JsonException ex)
        {
            Dispatch(new ErrorEvent("联机快照解析失败: " + ex.Message));
        }
    }

    private void OnError(PeerError error)
    {
        Volatile.Write(ref _lastError, error.Message);
        Dispatch(new ErrorEvent(error.Message));
    }

    private void OnCheat(string reason)
    {
        Volatile.Write(ref _lastError, reason);
        Dispatch(new ErrorEvent(reason));
    }

    private void Dispatch(UiEvent uiEvent)
    {
        Action<UiEvent>? handler = UiEvent;
        if (handler is null)
        {
            return;
        }

        if (_uiContext is null)
        {
            handler(uiEvent);
            return;
        }

        try
        {
            _uiContext.Post(_ => handler(uiEvent), null);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string Hash(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private static MatchPhase ParsePhase(string phase) => phase switch
    {
        "human_discard" => MatchPhase.HumanDiscard,
        "human_ron" => MatchPhase.HumanRon,
        "human_meld" => MatchPhase.HumanMeld,
        "round_end" => MatchPhase.RoundEnded,
        "match_over" => MatchPhase.MatchOver,
        _ => MatchPhase.Idle,
    };

    private static TableViewModel EmptySnapshot(int seat) => new()
    {
        Phase = "idle",
        Revision = 0,
        Seats = Array.Empty<SeatView>(),
        Message = "等待房主快照…",
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _peer.SnapshotReceived -= OnSnapshot;
        _peer.Error -= OnError;
        _peer.CheatDetected -= OnCheat;
    }
}
