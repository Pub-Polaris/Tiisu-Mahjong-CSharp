using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using Tiisu.App.Table;
using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Deal;
using Tiisu.Engine.Random;
using Tiisu.Engine.SelfPlay;
using CoreMeld = Tiisu.Core.Meld;
using EngineMeld = Tiisu.Engine.Meld;

namespace Tiisu.App.Runtime;

/// <summary>对局阶段（UI 用；Web 侧 <c>state</c> 的 human_ron / human_meld 由这里补上）。</summary>
public enum MatchPhase
{
    Idle,
    HumanDiscard,
    HumanRon,
    HumanMeld,
    RoundEnded,
    MatchOver,
}

/// <summary>一局的结果（供 UI / 统计 / 日志使用）。</summary>
public sealed record RoundResult(
    int Round,
    string Kind,
    IReadOnlyList<int> Winners,
    string? Reason,
    int[] Scores,
    int Honba,
    int DealerIndex,
    bool MatchOver,
    IReadOnlyList<SettlementDetail>? Details = null,
    string? DrawReason = null,
    IReadOnlyList<int>? ScoresBefore = null,
    IReadOnlyList<string>? Penalties = null);

/// <summary>局末判定的内部记录（决定写哪种日志 / 统计）。</summary>
internal sealed record RoundOutcome(string Kind, IReadOnlyList<int> Winners, string? Reason);

/// <summary>
/// 待决荣和。<see cref="Result"/> 为 null 表示「报听错和候选」：手牌成和形但赋不足起和线，
/// 点荣和会走错和判罚（<see cref="MisfireFu"/> 是那手的赋数）。
/// </summary>
internal sealed record PendingRon(Tile Tile, WinResult? Result, int Discarder, int MisfireFu = 0)
{
    public bool IsMisfire => Result is null;

    /// <summary>
    /// 待在手里的那张荣和牌是否已经临时加进手牌（真荣和 true；报听错和窗口 false）。
    /// 过牌时只能移除「确实在手里」的那张，否则会把玩家手牌删成小相公。
    /// </summary>
    public bool TrialInHand { get; init; }
}

internal sealed record PendingMeld(Tile Tile, int Discarder);

/// <summary>
/// 对局驱动（<c>engine.js</c> 的回合编排 + <c>game.js</c> 的人类操作语义）。
///
/// 调用顺序照 <c>SelfPlayDriver.Run</c>：<b>摸牌 → 自摸 → 暗杠/加杠（含岭上补摸与杠后自摸）→ 出牌 →
/// 特殊流局 → 他家荣和 → 杠/碰/吃 → nextTurn</b>；局末交给引擎的连庄 / 下庄 / 终局。
/// 只在一处与自走不同：人类回合停下来等命令（AI 回合照常自动打）。
///
/// 线程：本类不是线程安全的，只允许 <see cref="GameSession"/> 的引擎线程调用。
/// 每次状态变化通过 <c>emit</c> 抛 <see cref="UiEvent"/>，由 GameSession 负责 Marshal 回 UI。
/// </summary>
public sealed class MatchRuntime
{
    private static readonly YakuData SharedYaku = YakuData.LoadDefault();
    private static readonly PointsConfig SharedPoints = PointsConfig.LoadDefault();
    private static readonly HashSet<string> OptionalYakuNames = SharedYaku.Yaku
        .Where(y => y.Optional)
        .Select(y => y.Name)
        .ToHashSet(StringComparer.Ordinal);

    private readonly Settings _settings;
    private readonly IRandomSource _random;
    private readonly Action<UiEvent> _emit;
    private readonly SaveStore? _saveStore;
    private readonly StatsStore? _stats;
    private readonly RoundLogWriter? _roundLog;
    private readonly List<GameEvent> _events = new();
    private readonly List<ChiOption> _pendingChiList = new();

    /// <summary>四家 AI 档位（人类座位不参与，由 <see cref="AssignAiLineup"/> 每场重算）。</summary>
    private AiLevel[] _aiLevels = { AiLevel.Normal, AiLevel.Normal, AiLevel.Normal, AiLevel.Normal };

    private readonly int[] _aiDiscardDecisionsBySeat = new int[MahjongEngine.PlayerCount];
    private int _aiDiscardDecisions;
    private double _aiDecisionTotalMilliseconds;
    private readonly int[] _aiCallDeclarationsBySeat = new int[MahjongEngine.PlayerCount];
    private int _aiCallDeclarations;

    private MahjongEngine _engine;
    private WinChecker _checker;
    private int _modeTarget = 1;
    private bool _firstDeal = true;
    private MatchPhase _phase = MatchPhase.Idle;
    private RoundOutcome? _lastOutcome;
    private PendingRon? _pendingRon;
    private PendingMeld? _pendingMeld;
    private string? _message;
    private TableViewModel _snapshot;
    private bool _matchStatsRecorded;
    private int _rounds;
    private int _tsumoWins;
    private int _ronWins;
    private int _drawRounds;
    private int _specialFlows;
    private IReadOnlyList<SettlementDetail> _lastDetails = Array.Empty<SettlementDetail>();
    private int[] _scoresAtRoundStart = Array.Empty<int>();

    public MatchRuntime(
        Settings settings,
        IRandomSource? random,
        Action<UiEvent> emit,
        SaveStore? saveStore = null,
        StatsStore? stats = null,
        RoundLogWriter? roundLog = null,
        uint? matchSeed = null)
    {
        _settings = (settings ?? new Settings()).Clone();
        _random = random ?? new SharedRandomSource();
        _emit = emit ?? (_ => { });
        _saveStore = saveStore;
        _stats = stats;
        _roundLog = roundLog;

        // 本场种子：显式给了就用；否则从注入的随机源抽一次（同 seed 的 random 仍可复现）。
        MatchSeed = matchSeed ?? (uint)(_random.NextDouble() * uint.MaxValue);

        if (_roundLog is not null)
        {
            _roundLog.Enabled = _settings.RecordTiles;
        }

        _session = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        _checker = new WinChecker(SharedYaku, _settings.OptionalYaku);
        _engine = new MahjongEngine(SharedPoints, _checker, SharedYaku, _random);
        _engine.SetMode(_modeTarget);
        _engine.Players[0].IsHuman = !AllSeatsAi;
        _engine.Players[0].IsDealer = true;
        _engine.UpdateSeatWinds();
        _scoresAtRoundStart = _engine.Scores.ToArray();
        _snapshot = SnapshotBuilder.Build(_engine, PhaseWire, HumanAction.None, null, null, _settings, AiLevelWires());
    }

    private string _session;

    // ── 对外只读状态 ─────────────────────────────────────────────

    public MahjongEngine Engine => _engine;

    public MatchPhase Phase => _phase;

    public TableViewModel Snapshot => _snapshot;

    public string Session => _session;

    public int ModeTarget => _modeTarget;

    /// <summary>AutoPilot：人类回合自动出牌（摸什么打什么 / 能和则和、不副露）。smoke 用。</summary>
    public bool AutoPilot { get; set; }

    /// <summary>每个 AI 回合后的停顿（毫秒），让 Skia UI 看得到逐拍推进；0 = 全速。</summary>
    public int AiStepDelayMilliseconds { get; set; }

    public int Rounds => _rounds;

    public int TsumoWins => _tsumoWins;

    public int RonWins => _ronWins;

    public int DrawRounds => _drawRounds;

    public int SpecialFlowRounds => _specialFlows;

    public IReadOnlyList<int> FinalScores => _engine.Scores;

    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>事件流 SHA256（小写十六进制）；同 seed + 同命令序列必然一致。</summary>
    public string EventHash => SelfPlayResult.ComputeHash(_events);

    /// <summary>本场对局种子（AI 档位分配等派生用；同 seed 同分配）。</summary>
    public uint MatchSeed { get; }

    /// <summary>四家 AI 档位（人类座位为占位，不参与对局决策）。</summary>
    public IReadOnlyList<AiLevel> AiLevels => _aiLevels;

    /// <summary>本场走新 AI 的弃牌决策次数。</summary>
    public int AiDiscardDecisions => _aiDiscardDecisions;

    /// <summary>按座位统计的新 AI 弃牌决策次数（人类座位恒为 0）。</summary>
    public IReadOnlyList<int> AiDiscardDecisionsBySeat => _aiDiscardDecisionsBySeat;

    /// <summary>每次新 AI 弃牌决策的平均耗时（毫秒）；没有决策时为 0。</summary>
    public double AiDecisionAverageMilliseconds =>
        _aiDiscardDecisions == 0 ? 0.0 : _aiDecisionTotalMilliseconds / _aiDiscardDecisions;

    /// <summary>本场 AI 报听（叫胡）宣言次数。</summary>
    public int AiCallDeclarations => _aiCallDeclarations;

    /// <summary>按座位统计的 AI 报听宣言次数（人类座位通常为 0）。</summary>
    public IReadOnlyList<int> AiCallDeclarationsBySeat => _aiCallDeclarationsBySeat;

    /// <summary>smoke 用：取消人类座位，四家全部走 AI（须在对局开始前设置）。</summary>
    public bool AllSeatsAi { get; set; }

    // ── 命令入口 ─────────────────────────────────────────────────

    public void Execute(GameCommand command)
    {
        if (command is null)
        {
            return;
        }

        try
        {
            switch (command.Kind)
            {
                case GameCommandKind.StartMatch:
                    StartMatch(command.Mode);
                    break;
                case GameCommandKind.Resume:
                    Resume(command.Save);
                    break;
                case GameCommandKind.Discard:
                    HumanDiscard(command.Index);
                    break;
                case GameCommandKind.Chi:
                    HumanMeld(MeldType.Chi, command.Option);
                    break;
                case GameCommandKind.Pon:
                    HumanMeld(MeldType.Pon, 0);
                    break;
                case GameCommandKind.KanMinkan:
                    HumanMeld(MeldType.Minkan, 0);
                    break;
                case GameCommandKind.KanAn:
                    HumanKanSelf(MeldType.Ankan);
                    break;
                case GameCommandKind.KanKakan:
                    HumanKanSelf(MeldType.Kakan);
                    break;
                case GameCommandKind.Tsumo:
                    HumanTsumo();
                    break;
                case GameCommandKind.Ron:
                    HumanRon();
                    break;
                case GameCommandKind.DeclareCall:
                    HumanDeclareCall(command.Index);
                    break;
                case GameCommandKind.Pass:
                    Pass();
                    break;
                case GameCommandKind.Flow:
                    HumanFlow(command.Text);
                    break;
                case GameCommandKind.NextRound:
                    NextRound();
                    break;
                case GameCommandKind.ResetAll:
                    ResetAll();
                    break;
                case GameCommandKind.ApplySettings:
                    ApplySettings(command.Settings);
                    break;
                default:
                    Notify("未知命令：" + command.Kind, true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Emit(new ErrorEvent(ex.Message));
            EmitState();
        }
    }

    /// <summary><c>startMode(target)</c>：setMode → handNumber++ → startNewRound。</summary>
    public void StartMatch(int mode)
    {
        _modeTarget = Math.Max(1, mode);
        _engine.SetMode(_modeTarget);
        _firstDeal = true;
        ResetCounters();
        _roundLog?.Clear();
        _message = null;
        _engine.HandNumber++;
        ApplySeatModes();
        AssignAiLineup();
        StartNewRoundInternal();
        RunLoop();
    }

    /// <summary>人类座位开关（smoke 的 <c>--smoke-ai-call</c> 需要四家全 AI）。</summary>
    private void ApplySeatModes()
    {
        _engine.Players[0].IsHuman = !AllSeatsAi;
    }

    /// <summary>会话恢复：应用存档头 → handNumber++ → 开新局（与 Web 侧 <c>maybeResume</c> 同序）。</summary>
    public void Resume(SaveState? save)
    {
        if (save is null || !save.InProgress)
        {
            Notify("没有可恢复的存档", false);
            return;
        }

        _modeTarget = Math.Max(1, save.ModeTarget);
        _engine.SetMode(_modeTarget);
        EngineRestore.Apply(_engine, save);
        _firstDeal = false;
        ResetCounters();
        _roundLog?.Clear();

        if (_engine.HandNumber < 1)
        {
            _engine.HandNumber = 1;
        }
        else
        {
            _engine.HandNumber++;
        }

        AssignAiLineup();
        StartNewRoundInternal();
        Emit(new MessageEvent("已恢复存档继续 · " + _engine.RoundLabel(), false));
        RunLoop();    }

    /// <summary>完整重置（回到未开局）。</summary>
    public void ResetAll()
    {
        _pendingRon = null;
        _pendingMeld = null;
        _pendingChiList.Clear();
        _phase = MatchPhase.Idle;
        _lastOutcome = null;
        _firstDeal = true;
        _message = "已重置";
        _engine = new MahjongEngine(SharedPoints, _checker, SharedYaku, _random);
        _engine.SetMode(_modeTarget);
        _engine.Players[0].IsHuman = !AllSeatsAi;
        _engine.Players[0].IsDealer = true;
        _engine.UpdateSeatWinds();
        _scoresAtRoundStart = _engine.Scores.ToArray();
        ResetCounters();
        _roundLog?.Clear();
        EmitState();
        Emit(new MessageEvent("已重置", false));
    }

    /// <summary>hot-apply 设置（可选役 / recordTiles / 长考秒数）。</summary>
    public void ApplySettings(Settings? settings)
    {
        if (settings is null)
        {
            return;
        }

        _settings.CopyFrom(settings);
        if (_roundLog is not null)
        {
            _roundLog.Enabled = _settings.RecordTiles;
        }

        ApplyOptionalYaku();
        EmitState();
    }

    /// <summary><c>advanceRound</c> + 开下一局（局末按钮）。</summary>
    public void NextRound()
    {
        if (_engine.MatchOver || _phase == MatchPhase.MatchOver)
        {
            Notify("整场已结束", false);
            return;
        }

        if (!_engine.GameOver)
        {
            Notify("本局尚未结束", false);
            return;
        }

        _pendingRon = null;
        _pendingMeld = null;
        _pendingChiList.Clear();
        _lastOutcome = null;
        _message = null;
        _phase = MatchPhase.Idle;

        if (_engine.AdvanceRound())
        {
            _engine.HandNumber++;
            // JS saveSessionState 的时机：handsPlayed 已 +1、handNumber 仍是刚结束的那局。
            _saveStore?.SaveNow(CaptureSave(inProgress: true));
            StartNewRoundInternal();
            RunLoop();
            return;
        }

        _phase = MatchPhase.MatchOver;
        RecordMatchStatsOnce();
        _saveStore?.SaveNow(CaptureSave(inProgress: false));
        Emit(new MatchOverEvent("match-over"));
        EmitState();
    }

    /// <summary>构造当前可写入磁盘的存档（字段与 Web 侧同名）。</summary>
    public SaveState CaptureSave(bool inProgress) => new()
    {
        Session = _session,
        SavedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        PlayerUUID = string.Empty,
        ModeTarget = _modeTarget,
        HandsPlayed = _engine.HandsPlayed,
        HandNumber = _engine.HandNumber,
        Scores = _engine.Scores.ToArray(),
        DealerIndex = _engine.DealerIndex,
        Honba = _engine.Honba,
        RoundWindIdx = _engine.RoundWindIdx,
        DealerCount = _engine.DealerCount,
        InProgress = inProgress && !_engine.MatchOver,
        IsCalled = _engine.Players.Select(p => p.IsCalled).ToArray(),
        CallWinBanned = _engine.CallWinBanned.ToArray(),
        CallWinBannedNextHand = _engine.CallWinBannedNextHand.ToArray(),
    };

    // ── 主循环 ───────────────────────────────────────────────────

    /// <summary>一直推进到「需要人类决策」或「本局结束」。</summary>
    private void RunLoop()
    {
        int guard = 0;
        while (true)
        {
            if (++guard > 200_000)
            {
                throw new InvalidOperationException("对局动作数超过 200000，疑似死循环");
            }

            if (_phase is MatchPhase.RoundEnded or MatchPhase.MatchOver)
            {
                return;
            }

            if (_engine.GameOver)
            {
                FinishRound();
                return;
            }

            if (_pendingRon is not null)
            {
                _phase = MatchPhase.HumanRon;
                if (AutoPilot)
                {
                    AutoPilotStep();
                    continue;
                }

                EmitState();
                return;
            }

            if (_pendingMeld is not null)
            {
                _phase = MatchPhase.HumanMeld;
                if (AutoPilot)
                {
                    AutoPilotStep();
                    continue;
                }

                EmitState();
                return;
            }

            int turn = _engine.TurnIndex;
            var player = _engine.Players[turn];

            if (player.IsHuman)
            {
                if (!PrepareHumanTurn(player))
                {
                    return;
                }

                _phase = MatchPhase.HumanDiscard;
                if (AutoPilot)
                {
                    AutoPilotStep();
                    continue;
                }

                EmitState();
                return;
            }

            AiTurn();
            if (AiStepDelayMilliseconds > 0 && _phase == MatchPhase.Idle && !_engine.GameOver)
            {
                Thread.Sleep(AiStepDelayMilliseconds);
            }
        }
    }

    /// <summary>人类回合的准备：该摸就摸（JS <c>nextTurn</c> 里的 <c>humanDraw</c>）。</summary>
    private bool PrepareHumanTurn(Player player)
    {
        if (player.SkipDraw)
        {
            player.SkipDraw = false;
            return true;
        }

        if (player.IsDealer && player.Hand.Count == 14 && player.LastDraw is null)
        {
            player.FirstTurn = false;
            return true;
        }

        if (player.TotalTiles() == 13 + player.KongCount())
        {
            var drawn = _engine.DrawFromWall(player);
            if (drawn is null)
            {
                _lastOutcome = new RoundOutcome("flow", Array.Empty<int>(), "exhaustive");
                _engine.Flow();
                return false;
            }

            RecordEvent(GameEventTypes.Draw, player.Index, drawn.Value, "wall=" + _engine.Wall.Count);
            RecordStep(player.Index, "draw", drawn.Value);
            EmitState();
        }

        return true;
    }

    /// <summary>AI 的一个完整回合（<c>engine.js aiTurn</c>）。</summary>
    private void AiTurn()
    {
        int pi = _engine.TurnIndex;
        var player = _engine.Players[pi];

        bool isDealerFirst = player.IsDealer
                             && player.Hand.Count == 14
                             && player.LastDraw is null
                             && !player.SkipDraw;

        if (player.SkipDraw)
        {
            player.SkipDraw = false;
        }
        else if (!isDealerFirst)
        {
            var drawn = _engine.DrawFromWall(player);
            if (drawn is null)
            {
                _lastOutcome = new RoundOutcome("flow", Array.Empty<int>(), "exhaustive");
                _engine.Flow();
                return;
            }

            RecordEvent(GameEventTypes.Draw, pi, drawn.Value, "wall=" + _engine.Wall.Count);
            RecordStep(pi, "draw", drawn.Value);
            EmitState();
        }
        else
        {
            player.FirstTurn = false;
        }

        var observation = SelfPlayDriver.BuildObservation(_engine, pi);
        var tsumo = AiPlayer.CheckTsumo(observation, _checker);
        if (tsumo.Success && !_engine.IsWinBanned(player))
        {
            DeclareTsumo(pi, tsumo);
            return;
        }

        var kan = AiPlayer.DecideKan(observation);
        if (kan is not null && player.IsCalled
            && (kan.Type != MeldType.Ankan || !_engine.CallAllowsAnkan(player, kan.Tile, out _)))
        {
            // 报听后只允许「听牌形不变」的暗杠；加杠 / 会改听牌形的暗杠一律不尝试。
            kan = null;
        }

        if (kan is not null)
        {
            if (DoKanWithRobCheck(player, kan.Tile, kan.Type))
            {
                var afterKan = SelfPlayDriver.BuildObservation(_engine, pi);
                var tsumo2 = AiPlayer.CheckTsumo(afterKan, _checker);
                if (tsumo2.Success && !_engine.IsWinBanned(player))
                {
                    DeclareTsumo(pi, tsumo2);
                    return;
                }
            }
            else
            {
                return;
            }
        }

        var ctx = BuildAiDecisionContext(player);
        var aiDecision = DecideAiDiscard(player, ctx);
        int discardIndex = aiDecision.Index;

        bool declareCall = false;
        if (player.IsCalled)
        {
            // 报听后锁手：只能打「听牌张集合不变」的牌；优先按 AI 原选择，不行就摸切。
            discardIndex = EnsureCalledDiscard(player, discardIndex);
        }
        else
        {
            var call = AiCallPolicy.Evaluate(ctx, _aiLevels[pi], AiTuning.Default, PreferenceOrder(aiDecision));
            if (call.Declare && call.DiscardIndex >= 0)
            {
                discardIndex = call.DiscardIndex;
                declareCall = true;
            }
        }

        if (discardIndex < 0 || discardIndex >= player.Hand.Count)
        {
            _lastOutcome = new RoundOutcome("flow", Array.Empty<int>(), "exhaustive");
            _engine.Flow();
            return;
        }

        var discarded = _engine.Discard(player, player.Hand[discardIndex]);
        if (discarded is null)
        {
            _lastOutcome = new RoundOutcome("flow", Array.Empty<int>(), "exhaustive");
            _engine.Flow();
            return;
        }

        _phase = MatchPhase.Idle;
        RecordEvent(GameEventTypes.Discard, pi, discarded.Value, null);
        RecordStep(pi, "discard", discarded.Value);

        if (declareCall && _engine.DeclareCallWithHand(player, player.Hand))
        {
            RecordCall(pi, "discard");
            _aiCallDeclarations++;
            _aiCallDeclarationsBySeat[pi]++;
            _message = "已报听 · 听 " + string.Join(" ", SnapshotBuilder.TenpaiTileIds(_engine, player));
        }

        EmitState();
        AfterDiscard(discarded.Value, pi);
    }

    /// <summary>AI 报听候选的优先顺序：先按 AI 最终选择，再按评分排名。</summary>
    private static IReadOnlyList<int> PreferenceOrder(AiDecision decision)
    {
        var order = new List<int>(decision.Ranking.Count + 1);
        if (decision.Index >= 0)
        {
            order.Add(decision.Index);
        }

        foreach (var candidate in decision.Ranking)
        {
            if (candidate.Index != decision.Index)
            {
                order.Add(candidate.Index);
            }
        }

        return order;
    }

    /// <summary>报听锁手兜底：AI 的原选择若会改听牌形，退到摸切，再退到任意合法牌。</summary>
    private int EnsureCalledDiscard(Player player, int preferred)
    {
        if (preferred >= 0
            && preferred < player.Hand.Count
            && _engine.CallAllowsDiscard(player, player.Hand[preferred], out _))
        {
            return preferred;
        }

        if (player.LastDraw is Tile drawn)
        {
            int index = player.Hand.IndexOf(drawn);
            if (index >= 0 && _engine.CallAllowsDiscard(player, player.Hand[index], out _))
            {
                return index;
            }
        }

        for (int i = 0; i < player.Hand.Count; i++)
        {
            if (_engine.CallAllowsDiscard(player, player.Hand[i], out _))
            {
                return i;
            }
        }

        return preferred;
    }

    /// <summary>
    /// 按本场种子与设置分配四家 AI 档位；人类座位只占位。
    /// 权重 / 锁座来自 <see cref="Settings"/>，种子来自 <see cref="MatchSeed"/>（同 seed 同结果）。
    /// </summary>
    private void AssignAiLineup()
    {
        var humans = new bool[MahjongEngine.PlayerCount];
        for (int i = 0; i < humans.Length; i++)
        {
            humans[i] = _engine.Players[i].IsHuman;
        }

        var lineup = AiLineup.FromWeights(_settings.AiWeights, MatchSeed, _settings.AiSeatLocks, humans);
        _aiLevels = lineup.Levels.ToArray();
    }

    /// <summary>走新 AI（<see cref="TieredAi"/>）做一次弃牌决策，并累计耗时 / 次数。</summary>
    private AiDecision DecideAiDiscard(Player player, AiDecisionContext ctx)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            return TieredAi.Decide(ctx, _aiLevels[player.Index], _random);
        }
        finally
        {
            _aiDiscardDecisions++;
            _aiDiscardDecisionsBySeat[player.Index]++;
            _aiDecisionTotalMilliseconds +=
                (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }
    }

    /// <summary>用「该玩家可见」的信息构造新 AI 的弃牌输入（不含别家手牌 / 牌山）。</summary>
    private AiDecisionContext BuildAiDecisionContext(Player player)
    {
        var allDiscards = new List<IReadOnlyList<Tile>>(MahjongEngine.PlayerCount);
        var allMelds = new List<IReadOnlyList<EngineMeld>>(MahjongEngine.PlayerCount);
        var opponentMelds = new List<EngineMeld>();
        for (int seat = 0; seat < MahjongEngine.PlayerCount; seat++)
        {
            var other = _engine.Players[seat];
            allDiscards.Add(other.Discards);
            allMelds.Add(other.Melds);
            if (seat != player.Index)
            {
                opponentMelds.AddRange(other.Melds);
            }
        }

        return new AiDecisionContext(
            player.Hand,
            player.Melds,
            player.Discards,
            allDiscards,
            opponentMelds,
            _engine.Honba,
            _engine.Wall.Count,
            _engine.Scores.ToArray(),
            player.SeatWind,
            _engine.RoundWind,
            player.AnkanCount,
            player.MinkanCount)
        {
            PlayerIndex = player.Index,
            AllMelds = allMelds,
            MctsEnabled = _settings.AiMctsEnabled,
            FirstTurn = player.FirstTurn,
        };
    }

    /// <summary>快照调试字段用的档位名（与枚举同名，ASCII）。</summary>
    private IReadOnlyList<string> AiLevelWires() =>
        _aiLevels.Select(level => level.ToString()).ToArray();

    /// <summary>出牌后的反应链：特殊流局 → 人类荣和 → 他家荣和 → 杠/碰/吃（<c>afterDiscard</c>）。</summary>
    private void AfterDiscard(Tile discarded, int discPlayer)
    {
        if (_engine.CheckSpecialFlow())
        {
            _lastOutcome = new RoundOutcome("special_flow", Array.Empty<int>(), "four_wind_repeat");
            return;
        }

        int human = HumanIndex;
        if (human >= 0 && human != discPlayer && !_engine.IsWinBanned(_engine.Players[human]))
        {
            var result = TryRon(human, discarded);
            if (result is not null)
            {
                _pendingRon = new PendingRon(discarded, result, discPlayer) { TrialInHand = true };
                _pendingChiList.Clear();
                _phase = MatchPhase.HumanRon;
                _message = "荣和机会：别人打出 " + Display(discarded) + " · " + (result.TotalFu ?? 0) + "赋可和";
                return;
            }

            // 报听错和：手牌成和形但赋不足起和线，照样给荣和窗口（点了就走错和罚）。
            int misfireFu = RonMisfireFu(human, discarded);
            if (misfireFu > 0)
            {
                _pendingRon = new PendingRon(discarded, null, discPlayer, misfireFu);
                _pendingChiList.Clear();
                _phase = MatchPhase.HumanRon;
                _message = "报听错和风险：这手只有 " + misfireFu + " 赋（不足 " + _engine.BaseFu + "）· 点荣和按错和罚";
                return;
            }
        }

        var candidates = CheckRonForAi(discarded, discPlayer);
        if (candidates.Count > 0)
        {
            DeclareMultiRon(candidates, discPlayer, discarded);
            return;
        }

        CheckMelds(discarded, discPlayer);
    }

    private void CheckMelds(Tile discarded, int discPlayer)
    {
        int human = HumanIndex;
        if (human >= 0 && human != discPlayer && !_engine.Players[human].IsCalled)
        {
            var observation = SelfPlayDriver.BuildObservation(_engine, -1, discPlayer, discarded);
            var options = AiPlayer.GetMeldOptions(observation);
            bool canKan = options.Kan.Contains(human);
            bool canPon = options.Pon.Contains(human);
            bool canChi = options.Chi == human;
            if (canKan || canPon || canChi)
            {
                _pendingChiList.Clear();
                if (canChi)
                {
                    _pendingChiList.AddRange(AiPlayer.ChiOptions(_engine.Players[human].Hand, discarded));
                }

                _pendingMeld = new PendingMeld(discarded, discPlayer);
                _phase = MatchPhase.HumanMeld;
                _message = "副露机会：别人打出 " + Display(discarded);
                return;
            }
        }

        var meldObservation = SelfPlayDriver.BuildObservation(_engine, -1, discPlayer, discarded);
        var decision = AiPlayer.DecideMeld(meldObservation, _random.NextDouble());
        if (!decision.Applied)
        {
            NextTurnInternal();
            return;
        }

        // 报听后禁吃碰（含明杠）：AI 决策层直接避开，不依赖规则层抛异常。
        if (_engine.Players[decision.Target].IsCalled)
        {
            NextTurnInternal();
            return;
        }

        ApplyMeldCore(decision.Target, decision.Type, discarded, discPlayer, decision.ChiTrio);
    }

    private void NextTurnInternal()
    {
        _engine.NextTurn();
        if (_engine.GameOver && _lastOutcome is null)
        {
            _lastOutcome = new RoundOutcome("flow", Array.Empty<int>(), "exhaustive");
        }
    }

    // ── 人类操作（core：只做动作；public：动作 + 续跑）──────────

    public void HumanDiscard(int index)
    {
        if (!RequireHumanDiscard())
        {
            return;
        }

        if (DiscardCore(index))
        {
            RunLoop();
        }
    }

    public void HumanMeld(MeldType type, int chiOption)
    {
        if (_phase != MatchPhase.HumanMeld || _pendingMeld is null)
        {
            Notify("现在没有可副露的机会", false);
            return;
        }

        MeldCore(type, chiOption);
        RunLoop();
    }

    public void HumanKanSelf(MeldType type)
    {
        if (!RequireHumanDiscard())
        {
            return;
        }

        KanSelfCore(type);
        RunLoop();
    }

    public void HumanTsumo()
    {
        if (!RequireHumanDiscard())
        {
            return;
        }

        TsumoCore();
        RunLoop();
    }

    public void HumanRon()
    {
        if (_phase != MatchPhase.HumanRon || _pendingRon is null)
        {
            Notify("现在没有可荣和的牌", false);
            return;
        }

        RonCore();
        RunLoop();
    }

    /// <summary>
    /// 报听（叫胡）。出牌阶段：<paramref name="index"/> 是要打出的那张手牌，打出后立即宣言；
    /// 副露 / 荣和窗口：传 -1，按当前 13 张形判定。无二次确认、不可主动取消。
    /// </summary>
    public void HumanDeclareCall(int index)
    {
        int human = HumanIndex;
        if (human < 0)
        {
            return;
        }

        var player = _engine.Players[human];
        if (player.IsCalled)
        {
            Notify("已经报听，不能重复宣言", false);
            return;
        }

        // 副露窗口（吃碰杠）/ 荣和窗口：手牌已是 13 张形（荣和窗口临时留着待牌，按去掉待牌算）。
        if (_phase is MatchPhase.HumanMeld or MatchPhase.HumanRon)
        {
            var hand = _phase == MatchPhase.HumanRon && _pendingRon is PendingRon pending && !pending.IsMisfire
                ? WithoutTile(player.Hand, pending.Tile)
                : new List<Tile>(player.Hand);

            if (!_engine.DeclareCallWithHand(player, hand))
            {
                Notify("手牌没有听牌，不能报听", false);
                EmitState();
                return;
            }

            RecordCall(human, "window");
            _message = "已报听 · 听 " + string.Join(" ", SnapshotBuilder.TenpaiTileIds(_engine, player));
            EmitState();
            return;
        }

        if (_phase != MatchPhase.HumanDiscard || _engine.TurnIndex != human)
        {
            Notify("现在不能报听", false);
            return;
        }

        if (index < 0 || index >= player.Hand.Count)
        {
            Notify("请选择要打出的那张牌完成报听", false);
            return;
        }

        var tile = player.Hand[index];

        // 14 张（自家摸牌后）报听：这一步同时弃牌 + 锁手（牌形口径，不要求赋达起和线）。
        // 失败时不弃牌：按钮可用性与这里用同一套判定（MahjongEngine.CanDeclareCall）。
        if (!_engine.DeclareCallAfterDiscard(player, tile))
        {
            Notify("打出这张后不听牌，不能报听", false);
            EmitState();
            return;
        }

        _phase = MatchPhase.Idle;
        RecordEvent(GameEventTypes.Discard, human, tile, null);
        RecordStep(human, "discard", tile);

        RecordCall(human, "discard");
        _message = "已报听 · 听 " + string.Join(" ", SnapshotBuilder.TenpaiTileIds(_engine, player));
        EmitState();
        AfterDiscard(tile, human);

        // 出牌阶段的报听已经完成弃牌，必须自己把主循环续上：
        // AfterDiscard 只把回合交给下一家（或开副露 / 荣和窗口），不会替 AI 行动；
        // 漏掉这一句会让牌局停在 Idle —— 不能出牌、AI 也不行动。
        RunLoop();
    }

    /// <summary>报听事件：日志 / 回放事件名统一 <c>call</c>。</summary>
    private void RecordCall(int playerIndex, string via)
    {
        RecordEvent(GameEventTypes.Call, playerIndex, null, "call:" + via);
        RecordStep(playerIndex, "call", null, "via=" + via);
    }

    /// <summary>手牌去掉一张指定牌（不改原列表）。</summary>
    private static List<Tile> WithoutTile(IReadOnlyList<Tile> hand, Tile tile)
    {
        var copy = new List<Tile>(hand);
        int index = copy.IndexOf(tile);
        if (index >= 0)
        {
            copy.RemoveAt(index);
        }

        return copy;
    }

    public void Pass()
    {
        switch (_phase)
        {
            case MatchPhase.HumanRon:
                PassRonCore();
                RunLoop();
                break;
            case MatchPhase.HumanMeld:
                PassMeldCore();
                RunLoop();
                break;
            default:
                Notify("当前没有待决动作", false);
                break;
        }
    }

    public void HumanFlow(string? reason)
    {
        if (!RequireHumanDiscard())
        {
            return;
        }

        if (!FlowCore(reason))
        {
            return;
        }

        RunLoop();
    }

    private bool RequireHumanDiscard()
    {
        int human = HumanIndex;
        if (_phase != MatchPhase.HumanDiscard || human < 0 || _engine.TurnIndex != human)
        {
            Notify("现在不是你的出牌阶段", false);
            return false;
        }

        return true;
    }

    private bool DiscardCore(int index)
    {
        int human = HumanIndex;
        if (human < 0)
        {
            return false;
        }

        var player = _engine.Players[human];
        if (index < 0 || index >= player.Hand.Count)
        {
            Notify("出牌下标越界", false);
            return false;
        }

        // 报听锁手：打这张会改听牌张 → 拒绝并提示（引擎里还有一层兜底）。
        if (!_engine.CallAllowsDiscard(player, player.Hand[index], out string? callReason))
        {
            Notify(callReason ?? "报听后听牌张不能变", false);
            return false;
        }

        var discarded = _engine.Discard(player, player.Hand[index]);
        if (discarded is null)
        {
            Notify("这张牌不在手里", false);
            return false;
        }

        _phase = MatchPhase.Idle;
        _message = null;
        RecordEvent(GameEventTypes.Discard, human, discarded.Value, null);
        RecordStep(human, "discard", discarded.Value);
        EmitState();
        AfterDiscard(discarded.Value, human);
        return true;
    }

    private bool TsumoCore()
    {
        int human = HumanIndex;
        if (human < 0)
        {
            return false;
        }

        var player = _engine.Players[human];
        if (player.TotalTiles() != 14 + player.KongCount())
        {
            Notify("手牌不是 14 张，不能自摸", false);
            return false;
        }

        var observation = SelfPlayDriver.BuildObservation(_engine, human);
        var result = AiPlayer.CheckTsumo(observation, _checker);
        if (!result.Success)
        {
            // 报听错和：手牌成和形但赋不足起和线，玩家仍主动宣布和牌 → 判罚。
            if (IsMisfireResult(player, result) && !_engine.IsWinBanned(player))
            {
                ApplyMisfire(human, result.TotalFu ?? 0, selfDraw: true);
                if (_engine.MatchOver)
                {
                    _lastOutcome = new RoundOutcome("special_flow", Array.Empty<int>(), "call_foul");
                }

                _phase = MatchPhase.HumanDiscard;
                EmitState();
                return true;
            }

            Notify("手牌不能和", false);
            return false;
        }

        if (_engine.IsWinBanned(player))
        {
            Notify("本局禁和（或本巡禁和），不能宣布和牌", false);
            return false;
        }

        DeclareTsumo(human, result);
        return true;
    }

    /// <summary>错和候选：报听 + 手牌成和形但赋不足起和线（<c>no_yaku</c>）。</summary>
    private bool IsMisfireResult(Player player, WinResult result)
    {
        if (!player.IsCalled || result.Success || result.Reason != "no_yaku")
        {
            return false;
        }

        int fu = result.TotalFu ?? 0;
        return fu > 0 && fu < _engine.BaseFu;
    }

    /// <summary>
    /// 错和判罚落地：扣点 / 禁和标记 / 解除报听 / 事件与日志。返回 null 表示不构成错和。
    /// 是否终局（出岭）由调用方用 <see cref="MahjongEngine.MatchOver"/> 收口。
    /// </summary>
    private CallPenalty? ApplyMisfire(int playerIndex, int fu, bool selfDraw)
    {
        var penalty = _engine.ApplyCallMisfire(playerIndex, fu);
        if (penalty is null)
        {
            return null;
        }

        _message = penalty.Severe
            ? "报听错和（重罚）：赋 " + fu + " 不足起和线 · 本局 + 下局禁和"
            : "报听错和：赋 " + fu + " 不足起和线 · 向三家各付 " + penalty.Z;
        RecordEvent(
            GameEventTypes.Call,
            playerIndex,
            null,
            string.Format(
                CultureInfo.InvariantCulture,
                "call_foul:fu={0}:z={1}:{2}:{3}",
                fu,
                penalty.Z,
                penalty.Severe ? "severe" : "minor",
                selfDraw ? "tsumo" : "ron"));
        RecordStep(
            playerIndex,
            "call_foul",
            null,
            string.Format(CultureInfo.InvariantCulture, "fu={0}:z={1}", fu, penalty.Z));
        Emit(new MessageEvent(_message, true));
        _engine.CheckDeungnyeong();
        return penalty;
    }

    private bool KanSelfCore(MeldType requested)
    {
        int human = HumanIndex;
        if (human < 0)
        {
            return false;
        }

        var player = _engine.Players[human];
        var ankan = AiPlayer.CanAnkan(player.Hand);
        var kakan = ankan is null ? AiPlayer.CanKakan(ViewOf(player)) : null;

        Tile? tile = requested == MeldType.Ankan ? ankan : kakan;
        if (tile is null)
        {
            Notify(requested == MeldType.Ankan ? "没有可暗杠的牌" : "没有可加杠的牌", false);
            return false;
        }

        var type = requested == MeldType.Ankan ? MeldType.Ankan : MeldType.Kakan;
        if (!DoKanWithRobCheck(player, tile.Value, type))
        {
            return true; // 被抢杠 / 流局，交给 RunLoop 收口
        }

        _phase = MatchPhase.Idle;
        _message = null;
        var afterKan = SelfPlayDriver.BuildObservation(_engine, human);
        var tsumo = AiPlayer.CheckTsumo(afterKan, _checker);
        if (tsumo.Success)
        {
            DeclareTsumo(human, tsumo);
        }

        return true;
    }

    private bool RonCore()
    {
        if (_pendingRon is not PendingRon pending)
        {
            return false;
        }

        int human = HumanIndex;
        _pendingRon = null;
        _pendingChiList.Clear();
        _phase = MatchPhase.Idle;
        _message = null;

        var ronResult = pending.Result;
        if (ronResult is null)
        {
            // 报听错和：本次和牌不成立（错和家不算和），解除报听并判罚；
            // 同一张牌上其他家的真和牌照常结算（含双和子 / 过庄）。
            ApplyMisfire(human, pending.MisfireFu, selfDraw: false);
            if (_engine.MatchOver)
            {
                _lastOutcome = new RoundOutcome("special_flow", Array.Empty<int>(), "call_foul");
                return true;
            }

            EmitState();

            var others = CheckRonForAi(pending.Tile, pending.Discarder);
            if (others.Count > 0)
            {
                DeclareMultiRon(others, pending.Discarder, pending.Tile);
                return true;
            }

            CheckMelds(pending.Tile, pending.Discarder);
            return true;
        }

        var winners = new List<RonWinner>
        {
            new(human, SelfPlayDriver.ToSettlement(ronResult)),
        };

        foreach (var candidate in CheckRonForAi(pending.Tile, pending.Discarder))
        {
            var player = _engine.Players[candidate.PlayerIndex];
            player.Hand.Add(pending.Tile);
            player.Hand.Sort();
            winners.Add(new RonWinner(candidate.PlayerIndex, SelfPlayDriver.ToSettlement(candidate.Result)));
        }

        _lastOutcome = winners.Count >= 3
            ? new RoundOutcome("special_flow", Array.Empty<int>(), "triple_ron")
            : new RoundOutcome("ron", winners.Select(w => w.PlayerIndex).ToArray(), null);
        int honba = _engine.Honba;
        _engine.DeclareMultiRon(winners, pending.Discarder);
        CaptureRonDetails(pending.Discarder, honba);
        return true;
    }

    private bool PassRonCore()
    {
        if (_pendingRon is not PendingRon pending)
        {
            return false;
        }

        int human = HumanIndex;
        _pendingRon = null;
        _phase = MatchPhase.Idle;
        _message = null;

        // 只有「真荣和」窗口才把待荣和的牌临时放进过手牌；报听错和窗口没有放，
        // 这时不能移除任何牌，否则玩家的手牌会少一张（小相公）。
        var player = _engine.Players[human];
        if (pending.TrialInHand)
        {
            int pos = player.Hand.FindIndex(t => t == pending.Tile);
            if (pos >= 0)
            {
                player.Hand.RemoveAt(pos);
            }
        }

        var candidates = CheckRonForAi(pending.Tile, pending.Discarder);
        if (candidates.Count > 0)
        {
            DeclareMultiRon(candidates, pending.Discarder, pending.Tile);
            return true;
        }

        CheckMelds(pending.Tile, pending.Discarder);
        return true;
    }

    private bool PassMeldCore()
    {
        if (_pendingMeld is not PendingMeld pending)
        {
            return false;
        }

        _pendingMeld = null;
        _pendingChiList.Clear();
        _phase = MatchPhase.Idle;
        _message = null;

        var observation = SelfPlayDriver.BuildObservation(_engine, -1, pending.Discarder, pending.Tile);
        var decision = AiPlayer.DecideMeld(observation, _random.NextDouble());
        if (!decision.Applied)
        {
            NextTurnInternal();
            return true;
        }

        // 报听后禁吃碰（含明杠）：AI 决策层直接避开。
        if (_engine.Players[decision.Target].IsCalled)
        {
            NextTurnInternal();
            return true;
        }

        ApplyMeldCore(decision.Target, decision.Type, pending.Tile, pending.Discarder, decision.ChiTrio);
        return true;
    }

    private bool MeldCore(MeldType type, int chiOption)
    {
        if (_pendingMeld is not PendingMeld pending)
        {
            return false;
        }

        int human = HumanIndex;
        var observation = SelfPlayDriver.BuildObservation(_engine, -1, pending.Discarder, pending.Tile);
        var options = AiPlayer.GetMeldOptions(observation);
        bool allowed = type switch
        {
            MeldType.Chi => options.Chi == human,
            MeldType.Pon => options.Pon.Contains(human),
            MeldType.Minkan => options.Kan.Contains(human),
            _ => false,
        };

        _pendingMeld = null;
        if (!allowed)
        {
            _pendingChiList.Clear();
            _phase = MatchPhase.Idle;
            Notify("这个副露不可用", false);
            EmitState();
            return false;
        }

        IReadOnlyList<Tile>? trio = null;
        if (type == MeldType.Chi && _pendingChiList.Count > 0)
        {
            int idx = Math.Clamp(chiOption, 0, _pendingChiList.Count - 1);
            trio = _pendingChiList[idx].Trio;
        }

        _pendingChiList.Clear();
        _phase = MatchPhase.Idle;
        _message = null;
        ApplyMeldCore(human, type, pending.Tile, pending.Discarder, trio);
        return true;
    }

    private bool FlowCore(string? reason)
    {
        int human = HumanIndex;
        if (human < 0)
        {
            return false;
        }

        var options = _engine.FlowOptions(_engine.Players[human].Hand);
        if (options.Count == 0)
        {
            Notify("当前手牌不满足推牌流局条件", false);
            return false;
        }

        string picked = reason is not null && options.Contains(reason) ? reason : options[0];
        string code = picked switch
        {
            "九种九牌" => "nine_orphans",
            "十三不靠" => "thirteen_unrelated",
            _ => "special",
        };

        _phase = MatchPhase.Idle;
        _message = null;
        _lastOutcome = new RoundOutcome("flow", Array.Empty<int>(), code);
        _engine.DeclarePushFlow(picked, human);
        return true;
    }

    private void AutoPilotStep()
    {
        switch (_phase)
        {
            case MatchPhase.HumanRon:
                RonCore();
                return;

            case MatchPhase.HumanMeld:
                PassMeldCore();
                return;

            default:
                int human = HumanIndex;
                if (human < 0)
                {
                    _phase = MatchPhase.Idle;
                    return;
                }

                if ((AvailableActions() & HumanAction.Tsumo) != 0)
                {
                    TsumoCore();
                    return;
                }

                var player = _engine.Players[human];
                int index = player.LastDraw is Tile drawn ? player.Hand.IndexOf(drawn) : player.Hand.Count - 1;
                if (index < 0)
                {
                    index = player.Hand.Count - 1;
                }

                DiscardCore(index);
                return;
        }
    }

    // ── 结算 / 副露 / 杠 ─────────────────────────────────────────

    private void DeclareTsumo(int playerIndex, WinResult result)
    {
        _phase = MatchPhase.Idle;
        _message = null;
        bool sevenHonors = result.SevenHonors;
        _lastOutcome = new RoundOutcome(sevenHonors ? "tiisuin" : "tsumo", new[] { playerIndex }, null);
        var settlement = SelfPlayDriver.ToSettlement(result);
        int honba = _engine.Honba;
        _engine.DeclareWin(playerIndex, selfDraw: true, settlement);
        _lastDetails = sevenHonors || settlement.SevenHonors
            ? Array.Empty<SettlementDetail>()
            : new[] { BuildSettlementDetail(playerIndex, selfDraw: true, payerIndex: null, settlement, honba, null) };
    }

    private void DeclareMultiRon(IReadOnlyList<RonCandidate> candidates, int discPlayer, Tile discarded)
    {
        var winners = new List<RonWinner>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var player = _engine.Players[candidate.PlayerIndex];
            // engine.js checkRon：荣和张留在胜者暗手里（自走驱动同样这么加）
            player.Hand.Add(discarded);
            player.Hand.Sort();
            winners.Add(new RonWinner(candidate.PlayerIndex, SelfPlayDriver.ToSettlement(candidate.Result)));
        }

        _lastOutcome = candidates.Count >= 3
            ? new RoundOutcome("special_flow", Array.Empty<int>(), "triple_ron")
            : new RoundOutcome("ron", winners.Select(w => w.PlayerIndex).ToArray(), null);
        int honba = _engine.Honba;
        _engine.DeclareMultiRon(winners, discPlayer);
        CaptureRonDetails(discPlayer, honba);
    }

    /// <summary>
    /// 用引擎公开 API 现算一条结算明细。调用时机是引擎已完成结算（赐马已追加进
    /// <see cref="WinSettlement.ActiveYaku"/>），因此这里的数值与引擎实际扣加点数同一口径。
    /// </summary>
    private SettlementDetail BuildSettlementDetail(
        int winnerIndex,
        bool selfDraw,
        int? payerIndex,
        WinSettlement settlement,
        int honba,
        int? engineFinalPoints)
    {
        var player = _engine.Players[winnerIndex];
        var yakuRows = new List<(string Name, int Fu, SettlementYakuFlag Flag)>(settlement.ActiveYaku.Count);
        int maFu = 0;
        int tablelessFu = 0;
        int pointsBonus = 0;
        int? fixedPoints = null;

        foreach (var yaku in settlement.ActiveYaku)
        {
            SettlementYakuFlag flag;
            if (yaku.IsMa)
            {
                flag = SettlementYakuFlag.Ma;
                maFu += yaku.Fu;
                tablelessFu += yaku.Fu;
            }
            else if (yaku.FixedPoints is not null)
            {
                flag = SettlementYakuFlag.FixedPoints;
                fixedPoints ??= yaku.FixedPoints;
            }
            else if (yaku.IsBonus)
            {
                flag = SettlementYakuFlag.Tableless;
                tablelessFu += yaku.Fu;
            }
            else if (OptionalYakuNames.Contains(yaku.Name))
            {
                flag = SettlementYakuFlag.Optional;
            }
            else
            {
                flag = SettlementYakuFlag.Normal;
            }

            pointsBonus += yaku.PointsBonus ?? 0;
            yakuRows.Add((yaku.Name, yaku.Fu, flag));
        }

        int totalFu = settlement.TotalFu + maFu;
        int tableSafeFu = totalFu - tablelessFu;
        int basePoints;
        int tablelessBonus;
        if (fixedPoints is not null)
        {
            // 固定点数役：CalcFinalPoints 直接返回该值，不再叠加表外赋 / 加项。
            basePoints = fixedPoints.Value;
            tablelessBonus = 0;
            pointsBonus = 0;
        }
        else if (settlement.SpecialPoints is not null)
        {
            basePoints = settlement.SpecialPoints.Value;
            tablelessBonus = tablelessFu * 700;
        }
        else
        {
            basePoints = _engine.CalcPoints(tableSafeFu);
            tablelessBonus = tablelessFu * 700;
        }

        int subPoints = _engine.CalcFinalPoints(settlement, totalFu);
        bool dealer = player.IsDealer;
        int honbaBonus = 700 * honba;
        int finalPoints = engineFinalPoints
                          ?? ((dealer ? MahjongEngine.DealerBonus(subPoints) : subPoints) + honbaBonus);
        var (each, remainder) = _engine.SplitPayment(finalPoints);
        string payment = selfDraw
            ? $"自摸 · 每家 {each} + 余数 {remainder}"
            : $"荣和 · 放铳者付 {finalPoints}";

        return new SettlementDetail
        {
            WinnerIndex = winnerIndex,
            SelfDraw = selfDraw,
            PayerIndex = selfDraw ? null : payerIndex,
            Yaku = yakuRows,
            TotalFu = totalFu,
            MaFu = maFu,
            TableSafeFu = tableSafeFu,
            BasePoints = basePoints,
            TablelessBonus = tablelessBonus,
            PointsBonus = pointsBonus,
            SubPoints = subPoints,
            DealerMultiplierApplied = dealer,
            HonbaCount = honba,
            HonbaBonus = honbaBonus,
            FinalPoints = finalPoints,
            PayEach = selfDraw ? each : 0,
            PayRemainder = selfDraw ? remainder : 0,
            PaymentText = payment,
        };
    }

    /// <summary>多家荣和：以引擎最终生效的胜者列表（<see cref="MahjongEngine.LastRonMulti"/>）为准。</summary>
    private void CaptureRonDetails(int payerIndex, int honba)
    {
        if (_lastOutcome is null || _lastOutcome.Kind != "ron")
        {
            _lastDetails = Array.Empty<SettlementDetail>();
            return;
        }

        var details = new List<SettlementDetail>(_engine.LastRonMulti.Count);
        foreach (var winner in _engine.LastRonMulti)
        {
            details.Add(BuildSettlementDetail(
                winner.PlayerIndex,
                selfDraw: false,
                payerIndex,
                winner.Result,
                honba,
                winner.FinalPoints));
        }

        _lastDetails = details;
    }

    /// <summary>副露（AI 与人类共用），含明杠岭上补摸。</summary>
    private void ApplyMeldCore(int playerIndex, MeldType type, Tile tile, int discarder, IReadOnlyList<Tile>? chiTrio)
    {
        // 报听：禁吃碰明杠（规则层兜底提示，正常路径上面已经不给窗口）。
        if (!_engine.CallAllowsMeld(_engine.Players[playerIndex], type, out string? callReason))
        {
            Notify(callReason ?? "报听后禁止吃碰", false);
            EmitState();
            return;
        }

        // 副露后这张牌的荣和窗口就结束了：清掉遗留的待荣和状态，否则「和牌」按钮会一直亮着。
        _pendingRon = null;

        var player = _engine.Players[playerIndex];
        _engine.ApplyMeld(playerIndex, type, tile, discarder, chiTrio, _engine.PeekRinshanTile());

        if (type == MeldType.Minkan)
        {
            RecordEvent(GameEventTypes.Meld, playerIndex, tile, "minkan:from=" + discarder);
            RecordStep(playerIndex, "kan_minkan", tile);
            if (!_engine.GameOver && _engine.CurrentDrawn is Tile drawn)
            {
                RecordEvent(GameEventTypes.Kan, playerIndex, drawn, "minkan:rinshan=" + (_engine.Rinshan.Count > 0 ? "yes" : "wall"));
                RecordStep(playerIndex, "rinshan_draw", drawn, via: _engine.Rinshan.Count == 0 ? "wall" : "rinshan");
            }
        }
        else
        {
            string wire = type.Wire();
            RecordEvent(GameEventTypes.Meld, playerIndex, tile, wire + ":from=" + discarder);
            RecordStep(playerIndex, wire, tile);
        }

        if (_engine.GameOver && _lastOutcome is null)
        {
            _lastOutcome = new RoundOutcome("flow", Array.Empty<int>(), "exhaustive");
        }

        _phase = player.IsHuman ? MatchPhase.HumanDiscard : MatchPhase.Idle;
        EmitState();
    }

    /// <summary>抢杠检查 + 执行杠（含抢杠 / 三家和牌 / 牌山耗尽流局）。</summary>
    private bool DoKanWithRobCheck(Player player, Tile tile, MeldType type)
    {
        var robbers = CheckRobKan(tile, type, player.Index);
        if (robbers.Count > 0)
        {
            if (robbers.Count >= 3)
            {
                _lastOutcome = new RoundOutcome("special_flow", Array.Empty<int>(), "triple_ron_chankan");
                _engine.DeclareSpecialFlow("三家和牌（抢杠）→ 自动流局");
                return false;
            }

            var winners = robbers
                .Select(candidate => new RonWinner(candidate.PlayerIndex, SelfPlayDriver.ToSettlement(candidate.Result)))
                .ToList();
            _lastOutcome = new RoundOutcome("ron", winners.Select(w => w.PlayerIndex).ToArray(), null);
            int honba = _engine.Honba;
            _engine.DeclareMultiRon(winners, player.Index);
            CaptureRonDetails(player.Index, honba);
            return false;
        }

        if (!_engine.DoKan(player, tile, type, _engine.PeekRinshanTile()))
        {
            _lastOutcome ??= new RoundOutcome("flow", Array.Empty<int>(), "exhaustive");
            return false;
        }

        var drawn = _engine.CurrentDrawn ?? tile;
        string action = type == MeldType.Ankan ? "kan_ankan" : "kan_kakan";
        RecordEvent(GameEventTypes.Kan, player.Index, drawn, type.Wire() + ":rinshan=" + (_engine.Rinshan.Count > 0 ? "yes" : "wall"));
        RecordStep(player.Index, action, drawn, via: _engine.Rinshan.Count == 0 ? "wall" : "rinshan");
        EmitState();
        return true;
    }

    /// <summary>人类荣和试探（<c>tryRon</c>）：成功时荣和张留在手里。</summary>
    private WinResult? TryRon(int playerIndex, Tile tile)
    {
        var player = _engine.Players[playerIndex];
        int meldTiles = 0;
        int kongs = 0;
        foreach (var meld in player.Melds)
        {
            meldTiles += meld.Tiles.Count;
            if (meld.Tiles.Count == 4)
            {
                kongs++;
            }
        }

        if (player.Hand.Count != 13 - meldTiles + kongs)
        {
            return null;
        }

        player.Hand.Add(tile);
        player.Hand.Sort();

        var result = _checker.CheckWin(
            player.Hand,
            selfDraw: false,
            hasCalled: player.Melds.Count > 0 || player.MinkanCount > 0,
            ankanCount: player.AnkanCount,
            minkanCount: player.MinkanCount,
            melds: ToCoreMelds(player.Melds),
            firstTurn: player.FirstTurn,
            winTile: tile,
            context: BuildWinContext(playerIndex, player.Hand, selfDraw: false, tile));

        if (!result.Success)
        {
            int pos = player.Hand.FindIndex(t => t == tile);
            if (pos >= 0)
            {
                player.Hand.RemoveAt(pos);
            }

            return null;
        }

        return result;
    }

    /// <summary>
    /// 报听荣和错和候选：手牌加上这张牌后成和形、但赋数不足起和线时返回该手赋数；否则返回 0。
    /// 判定用的牌临时加进手牌再移走，不改对局状态。
    /// </summary>
    private int RonMisfireFu(int playerIndex, Tile tile)
    {
        var player = _engine.Players[playerIndex];
        if (!player.IsCalled)
        {
            return 0;
        }

        if (player.Hand.Count != 13 - player.MeldTiles() + player.KongCount())
        {
            return 0;
        }

        player.Hand.Add(tile);
        player.Hand.Sort();

        var result = _checker.CheckWin(
            player.Hand,
            selfDraw: false,
            hasCalled: player.Melds.Count > 0 || player.MinkanCount > 0,
            ankanCount: player.AnkanCount,
            minkanCount: player.MinkanCount,
            melds: ToCoreMelds(player.Melds),
            firstTurn: player.FirstTurn,
            winTile: tile,
            context: BuildWinContext(playerIndex, player.Hand, selfDraw: false, tile));

        int position = player.Hand.FindIndex(t => t == tile);
        if (position >= 0)
        {
            player.Hand.RemoveAt(position);
        }

        if (result.Success || result.Reason != "no_yaku")
        {
            return 0;
        }

        int fu = result.TotalFu ?? 0;
        return fu > 0 && fu < _engine.BaseFu ? fu : 0;
    }

    /// <summary>抢杠（<c>checkRobKan</c>）：加杠可被任意家抢；暗杠只有国士形能抢。</summary>
    private List<RonCandidate> CheckRobKan(Tile tile, MeldType type, int kanPlayer)
    {
        var winners = new List<RonCandidate>();
        for (int pi = 0; pi < MahjongEngine.PlayerCount; pi++)
        {
            if (pi == kanPlayer)
            {
                continue;
            }

            var player = _engine.Players[pi];
            if (type == MeldType.Ankan && !IsKokushiShape(player))
            {
                continue;
            }

            player.Hand.Add(tile);
            player.Hand.Sort();

            var result = _checker.CheckWin(
                player.Hand,
                selfDraw: false,
                hasCalled: player.Melds.Count > 0 || player.MinkanCount > 0,
                ankanCount: player.AnkanCount,
                minkanCount: player.MinkanCount,
                melds: ToCoreMelds(player.Melds),
                firstTurn: player.FirstTurn,
                winTile: tile,
                context: BuildWinContext(pi, player.Hand, selfDraw: false, tile));

            if (result.Success)
            {
                winners.Add(new RonCandidate(pi, result));
            }
            else
            {
                int pos = player.Hand.FindIndex(t => t == tile);
                if (pos >= 0)
                {
                    player.Hand.RemoveAt(pos);
                }
            }
        }

        return winners;
    }

    private List<RonCandidate> CheckRonForAi(Tile discarded, int discPlayer)
    {
        var observation = SelfPlayDriver.BuildObservation(_engine, -1, discPlayer, discarded);
        var candidates = AiPlayer.CheckRonDetailed(observation, _checker);
        if (candidates.Count == 0)
        {
            return candidates;
        }

        // 禁和标记（错和罚）同样约束 AI。
        var allowed = new List<RonCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (!_engine.IsWinBanned(_engine.Players[candidate.PlayerIndex]))
            {
                allowed.Add(candidate);
            }
        }

        return allowed;
    }

    private static bool IsKokushiShape(Player player)
    {
        var kinds = new HashSet<Tile>();
        foreach (var tile in player.Hand)
        {
            if (tile.IsHonor)
            {
                kinds.Add(tile);
            }
            else if (tile.Num is 1 or 9)
            {
                kinds.Add(tile);
            }
            else
            {
                return false;
            }
        }

        return kinds.Count >= 12;
    }

    // ── 局末 ─────────────────────────────────────────────────────

    private void FinishRound()
    {
        if (_phase is MatchPhase.RoundEnded or MatchPhase.MatchOver)
        {
            return;
        }

        var outcome = _lastOutcome ?? new RoundOutcome("flow", Array.Empty<int>(), "exhaustive");

        switch (outcome.Kind)
        {
            case "tsumo":
                RecordWinRound("win_tsumo", "tsumo", outcome.Winners);
                _tsumoWins++;
                break;
            case "ron":
                RecordWinRound("win_ron", "ron", outcome.Winners);
                _ronWins += outcome.Winners.Count;
                break;
            case "tiisuin":
                RecordWinRound("tiisuin_win", "tiisuin", outcome.Winners);
                break;
            case "special_flow":
                RecordFlowRound("special_flow", outcome.Reason ?? "special");
                _drawRounds++;
                _specialFlows++;
                break;
            default:
                RecordFlowRound("flow", outcome.Reason ?? "exhaustive");
                _drawRounds++;
                break;
        }

        _rounds++;
        _roundLog?.Flush(_engine, _session);

        var names = _engine.Players.Select(p => p.Name).ToArray();
        bool winOutcome = outcome.Kind is "tsumo" or "ron";
        var details = winOutcome ? _lastDetails : Array.Empty<SettlementDetail>();
        string? drawReason = outcome.Kind is "flow" or "special_flow"
            ? outcome.Reason ?? "exhaustive"
            : null;
        var result = new RoundResult(
            _engine.HandNumber,
            outcome.Kind,
            outcome.Winners,
            outcome.Reason,
            _engine.Scores.ToArray(),
            _engine.Honba,
            _engine.DealerIndex,
            _engine.MatchOver,
            details,
            drawReason,
            _scoresAtRoundStart.ToArray(),
            outcome.Kind is "flow" or "special_flow" ? _engine.LastFlowPenalties : null);

        _stats?.RecordRound(result, names);
        RecordEvent(
            GameEventTypes.RoundEnd,
            -1,
            null,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}:scores={1}:honba={2}:dealer={3}",
                outcome.Kind.ToLowerInvariant(),
                string.Join(",", _engine.Scores),
                _engine.Honba,
                _engine.DealerIndex));

        _pendingRon = null;
        _pendingMeld = null;
        _pendingChiList.Clear();
        Emit(new RoundEndEvent(result));

        if (_engine.MatchOver)
        {
            _phase = MatchPhase.MatchOver;
            RecordMatchStatsOnce();
            _saveStore?.SaveNow(CaptureSave(inProgress: false));
            string reason = outcome.Kind == "tiisuin"
                ? "tiisuin"
                : _engine.Scores.Contains(-7000) ? "bust" : "match-over";
            _message = reason;
            Emit(new MatchOverEvent(reason));
        }
        else
        {
            _phase = MatchPhase.RoundEnded;
        }

        EmitState();
    }

    private void RecordWinRound(string action, string kind, IReadOnlyList<int> winners)
    {
        for (int pi = 0; pi < MahjongEngine.PlayerCount; pi++)
        {
            bool won = winners.Contains(pi);
            if (won)
            {
                RecordEvent(GameEventTypes.Win, pi, null, kind);
            }

            string step = kind == "tiisuin"
                ? (won ? "tiisuin_win" : "witness")
                : (won ? action : "observe");
            RecordStep(pi, step);
        }
    }

    private void RecordFlowRound(string action, string reason)
    {
        RecordEvent(GameEventTypes.Flow, -1, null, reason);
        for (int pi = 0; pi < MahjongEngine.PlayerCount; pi++)
        {
            RecordStep(pi, action, null, reason);
        }
    }

    private void RecordMatchStatsOnce()
    {
        if (_matchStatsRecorded)
        {
            return;
        }

        _matchStatsRecorded = true;
        _stats?.RecordMatchEnd(_engine.Scores, _engine.Players.Select(p => p.Name).ToArray());
    }

    // ── 快照 / 动作 / 事件 ───────────────────────────────────────

    private string PhaseWire => _phase switch
    {
        MatchPhase.HumanDiscard => "human_discard",
        MatchPhase.HumanRon => "human_ron",
        MatchPhase.HumanMeld => "human_meld",
        MatchPhase.RoundEnded => "round_end",
        MatchPhase.MatchOver => "match_over",
        _ => "idle",
    };

    /// <summary>当前可用动作（按钮出现条件与优先级：杠 &gt; 和 &gt; 碰 &gt; 吃 &gt; 过）。</summary>
    public HumanAction AvailableActions()
    {
        var actions = HumanAction.None;
        int human = HumanIndex;
        if (human < 0)
        {
            return actions;
        }

        switch (_phase)
        {
            case MatchPhase.HumanDiscard:
            {
                actions |= HumanAction.Discard;
                var player = _engine.Players[human];
                bool banned = _engine.IsWinBanned(player);
                if (player.TotalTiles() == 14 + player.KongCount())
                {
                    var observation = SelfPlayDriver.BuildObservation(_engine, human);
                    var tsumo = AiPlayer.CheckTsumo(observation, _checker);
                    if (tsumo.Success)
                    {
                        if (!banned)
                        {
                            actions |= HumanAction.Tsumo;
                        }
                    }
                    else if (!banned && IsMisfireResult(player, tsumo))
                    {
                        // 报听错和候选：手牌成和形但赋不足起和线，仍允许「宣布和牌」。
                        actions |= HumanAction.Tsumo;
                    }
                }

                var ankan = AiPlayer.CanAnkan(player.Hand);
                if (ankan is not null)
                {
                    // 报听：只有听牌形不变的暗杠可点。
                    if (!player.IsCalled || _engine.CallAllowsAnkan(player, ankan.Value, out _))
                    {
                        actions |= HumanAction.Kan;
                    }
                }
                else if (!player.IsCalled && AiPlayer.CanKakan(ViewOf(player)) is not null)
                {
                    actions |= HumanAction.Kan;
                }

                if (_engine.FlowOptions(player.Hand).Count > 0)
                {
                    actions |= HumanAction.Flow;
                }

                // 报听可用性统一由引擎判定：13 张=真听牌；14 张=存在「打出后成听牌形」的弃牌。
                if (!player.IsCalled && _engine.CanDeclareCall(player))
                {
                    actions |= HumanAction.DeclareCall;
                }

                break;
            }

            case MatchPhase.HumanRon:
                actions |= HumanAction.Ron | HumanAction.Pass;
                if (!_engine.Players[human].IsCalled && _pendingRon is PendingRon ron && !ron.IsMisfire
                    && ReturnsTenpai(_engine.Players[human], ron.Tile))
                {
                    actions |= HumanAction.DeclareCall;
                }

                break;

            case MatchPhase.HumanMeld:
                if (_pendingMeld is PendingMeld pending)
                {
                    var player = _engine.Players[human];
                    var observation = SelfPlayDriver.BuildObservation(_engine, -1, pending.Discarder, pending.Tile);
                    var options = AiPlayer.GetMeldOptions(observation);

                    // 报听后禁止吃碰（含明杠），只留「过」。
                    if (!player.IsCalled)
                    {
                        if (options.Kan.Contains(human))
                        {
                            actions |= HumanAction.Kan;
                        }

                        if (options.Pon.Contains(human))
                        {
                            actions |= HumanAction.Pon;
                        }

                        if (options.Chi == human)
                        {
                            actions |= HumanAction.Chi;
                        }
                    }

                    actions |= HumanAction.Pass;

                    if (!player.IsCalled && _engine.CanDeclareCall(player))
                    {
                        actions |= HumanAction.DeclareCall;
                    }
                }

                break;
        }

        return actions;
    }

    /// <summary>荣和窗口里「去掉待牌」后是否已是听牌形（报听按钮用）。</summary>
    private bool ReturnsTenpai(Player player, Tile pendingTile)
    {
        var hand = WithoutTile(player.Hand, pendingTile);
        if (hand.Count + player.MeldTiles() != 13 + player.KongCount())
        {
            return false;
        }

        return _engine.WaitMaskFor(
            hand, player.Melds, player.AnkanCount, player.MinkanCount, player.SeatWind, player.FirstTurn) != 0;
    }

    /// <summary>吃牌多选的按钮文案（如「吃 234m」）。</summary>
    public IReadOnlyList<string> ActionLabels()
    {
        if (_phase != MatchPhase.HumanMeld || _pendingChiList.Count == 0)
        {
            return Array.Empty<string>();
        }

        var labels = new List<string>(_pendingChiList.Count);
        foreach (var option in _pendingChiList)
        {
            var trio = option.Trio;
            if (trio.Count == 0)
            {
                continue;
            }

            var nums = string.Concat(trio.Select(t => t.Num.ToString(CultureInfo.InvariantCulture)));
            labels.Add("吃 " + nums + TileNotation.SuitChar(trio[0].Suit));
        }

        return labels;
    }

    private void EmitState()
    {
        _snapshot = SnapshotBuilder.Build(
            _engine,
            PhaseWire,
            AvailableActions(),
            ActionLabels(),
            _message,
            _settings,
            AiLevelWires());

        _saveStore?.Save(CaptureSave(!_engine.MatchOver));
        Emit(new StateChangedEvent(_snapshot));
    }

    private void Emit(UiEvent uiEvent)
    {
        try
        {
            _emit(uiEvent);
        }
        catch
        {
            // UI 侧异常不能拖死引擎线程。
        }
    }

    private void Notify(string text, bool important) => Emit(new MessageEvent(text, important));

    private void RecordEvent(string type, int playerIndex, Tile? tile, string? detail)
    {
        _events.Add(new GameEvent
        {
            Type = type,
            Round = _engine.HandNumber,
            Player = playerIndex,
            Tile = tile is Tile value ? TileNotation.TileToStr(value) : null,
            Detail = detail,
        });
    }

    private void RecordStep(int playerIndex, string action, Tile? tile = null, string? reason = null, string? via = null)
    {
        _roundLog?.Step(_engine, playerIndex, action, tile, reason, via);
    }

    // ── 共通 ─────────────────────────────────────────────────────

    public int HumanIndex
    {
        get
        {
            for (int i = 0; i < MahjongEngine.PlayerCount; i++)
            {
                if (_engine.Players[i].IsHuman)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    private void StartNewRoundInternal()
    {
        _pendingRon = null;
        _pendingMeld = null;
        _pendingChiList.Clear();
        _lastOutcome = null;
        _lastDetails = Array.Empty<SettlementDetail>();
        _scoresAtRoundStart = _engine.Scores.ToArray();
        _phase = MatchPhase.Idle;
        _message = null;
        _roundLog?.Clear();

        int dealer = _engine.DealerIndex;
        if (_firstDeal)
        {
            _firstDeal = false;
            DiceDealer = TileSetFactory.RollDice(_random);
            dealer = TileSetFactory.DetermineDealer(DiceDealer);
        }

        var layout = RoundDealFactory.Cut(dealer, _random);
        DiceCut = layout.Dice;
        _engine.SetDiceInfo(DiceDealer, DiceCut, layout.SkipTiles);
        _engine.StartNewRound(layout.Deal);
        EmitState();
    }

    /// <summary>本场首次定庄骰（与 Web 侧 <c>game.diceDealer</c> 同义）。</summary>
    public IReadOnlyList<int>? DiceDealer { get; private set; }

    /// <summary>本局切牌骰。</summary>
    public IReadOnlyList<int>? DiceCut { get; private set; }

    private void ResetCounters()
    {
        _events.Clear();
        _rounds = 0;
        _tsumoWins = 0;
        _ronWins = 0;
        _drawRounds = 0;
        _specialFlows = 0;
        _matchStatsRecorded = false;
        _lastOutcome = null;
        _lastDetails = Array.Empty<SettlementDetail>();
        _pendingRon = null;
        _pendingMeld = null;
        _pendingChiList.Clear();
        Array.Clear(_aiCallDeclarationsBySeat);
        _aiCallDeclarations = 0;
    }

    private void ApplyOptionalYaku()
    {
        var field = typeof(WinChecker).GetField("_optionalEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(_checker) is HashSet<string> set)
        {
            set.Clear();
            foreach (var id in _settings.OptionalYaku)
            {
                set.Add(id);
            }
        }
    }

    private WinContext BuildWinContext(int playerIndex, IReadOnlyList<Tile> effectiveHand, bool selfDraw, Tile? winTile)
    {
        int visible = 0;
        if (winTile is Tile target)
        {
            // 和绝张口径：可见张数 = 四家弃牌河 + 四家副露（含杠）里该牌的数量；手牌不计。
            // 荣和时放铳牌已在弃牌河里，它就是和牌张本身，统计时排除这一张。
            int riverCount = 0;
            foreach (var player in _engine.Players)
            {
                riverCount += Count(player.Discards, target);
                foreach (var meld in player.Melds)
                {
                    visible += Count(meld.Tiles, target);
                }
            }

            visible += riverCount;
            if (!selfDraw && riverCount > 0)
            {
                visible--;
            }
        }

        bool anyCalled = false;
        foreach (var player in _engine.Players)
        {
            if (player.Melds.Count > 0 || player.MinkanCount > 0)
            {
                anyCalled = true;
                break;
            }
        }

        var self = _engine.Players[playerIndex];
        bool renhe = !selfDraw && !self.IsDealer && !anyCalled && self.FirstTurn && self.LastDraw is null;
        bool waterDrop = self.FirstTurnTenpai && (selfDraw ? self.HaiDei : _engine.Wall.Count == 0);

        return new WinContext
        {
            SeaMoon = selfDraw && self.SeaMoon,
            HaiDei = selfDraw && self.HaiDei,
            RiverFish = !selfDraw && _engine.Wall.Count == 0,
            LastTileVisible = visible,
            RinshanFlower = selfDraw && self.RinshanDrawn,
            RinshanGun = !selfDraw && _engine.KanThisTurn,
            KanRiichi = _engine.KanThisTurn,
            Renhe = renhe,
            WaterDrop = waterDrop,
            SeatWind = self.SeatWind.Wire(),
            RoundWind = _engine.RoundWind.Wire(),
        };
    }

    private static int Count(IReadOnlyList<Tile> tiles, Tile target)
    {
        int count = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i] == target)
            {
                count++;
            }
        }

        return count;
    }

    private static List<CoreMeld> ToCoreMelds(IEnumerable<EngineMeld> melds) =>
        melds.Select(meld => new CoreMeld(meld.Type.Wire(), meld.Tiles.ToList())).ToList();

    private static AiPlayerView ViewOf(Player player)
    {
        var view = new AiPlayerView
        {
            SeatWind = player.SeatWind,
            IsDealer = player.IsDealer,
            IsHuman = player.IsHuman,
            FirstTurn = player.FirstTurn,
            AnkanCount = player.AnkanCount,
            MinkanCount = player.MinkanCount,
            LastDraw = player.LastDraw,
            SeaMoon = player.SeaMoon,
            HaiDei = player.HaiDei,
            RinshanDrawn = player.RinshanDrawn,
            FirstTurnTenpai = player.FirstTurnTenpai,
            SkipDraw = player.SkipDraw,
        };

        view.Hand.AddRange(player.Hand);
        view.Discards.AddRange(player.Discards);
        foreach (var meld in player.Melds)
        {
            view.Melds.Add(new EngineMeld(meld.Type, meld.Tiles));
        }

        return view;
    }

    private static string Display(Tile tile) => tile.Suit switch
    {
        Suit.Man => tile.Num + "万",
        Suit.Pin => tile.Num + "饼",
        Suit.Sou => tile.Num + "索",
        _ => tile.Num switch
        {
            1 => "东",
            2 => "南",
            3 => "西",
            4 => "北",
            5 => "白",
            6 => "发",
            7 => "中",
            _ => tile.Num.ToString(CultureInfo.InvariantCulture),
        },
    };

    private sealed class SharedRandomSource : IRandomSource
    {
        public double NextDouble() => Random.Shared.NextDouble();
    }
}
