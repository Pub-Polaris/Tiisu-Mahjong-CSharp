using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tiisu.Api;
using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Deal;
using Tiisu.Engine.Random;
using Tiisu.Engine.SelfPlay;
using CoreMeld = Tiisu.Core.Meld;
using EngineMeld = Tiisu.Engine.Meld;

namespace Tiisu.Shell;

/// <summary>
/// C# 引擎宿主对象（<c>chrome.webview.hostObjects.sync.tiisu</c>）。
///
/// 对 JS 暴露与 <c>Web/src/engine.js</c> 的 <c>TiisuMahjong</c> 同名同参的方法；
/// 每次修改型调用返回完整状态快照（<see cref="GetStateJson"/>），由
/// <c>Web/js/host_adapter.js</c> 贴回 JS 镜像对象。
///
/// 状态机分工：
/// <list type="bullet">
/// <item><see cref="MahjongEngine"/> 负责规则/计分（Core + Engine 的既有实现）；</item>
/// <item>本类负责 <c>engine.js</c> 的「回合编排」：AI 摸打、荣和/副露窗口、人类操作、下一局调度；</item>
/// <item>AI 决策直接复用 <see cref="AiPlayer"/>（与自走/语料同一实现）。</item>
/// </list>
/// 所有方法都在 UI 线程（WebView2 host object 的 COM 调度线程）上执行，无锁。
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed partial class EngineHost
{
    private static readonly string[] Seats = { "east", "south", "west", "north" };
    private static readonly string[] AiUuidLasts = { "1111", "2222", "3333" };

    private readonly YakuData _yaku;
    private readonly PointsConfig _points;
    private readonly IRandomSource _random = new SharedRandomSource();
    private readonly string[] _uuids = new string[MahjongEngine.PlayerCount];

    private MahjongEngine _engine;
    private WinChecker _checker;
    private JsonObject _settings = new();
    private string _playerUUID = string.Empty;

    private bool _firstDeal = true;
    private int[]? _diceDealer;
    private int[]? _diceCut;
    private JsonObject? _cutInfo;

    private int _modeTarget = 1;
    private int _speed = 250;
    private bool _recordTiles;
    private bool _allAI;
    private bool _demoMode;

    /// <summary>JS 的 <c>human_ron</c> / <c>human_meld</c> 不在 C# <see cref="EngineState"/> 里，用覆盖值表达。</summary>
    private string? _stateOverride;

    private PendingRonState? _pendingRon;
    private PendingMeldState? _pendingMeld;
    private List<ChiOption> _pendingChiList = new();

    private JsonObject? _actOpts;
    private string _actMsg = string.Empty;
    private int _actLeft;

    public EngineHost()
    {
        _yaku = YakuData.LoadDefault();
        _points = PointsConfig.LoadDefault();
        _checker = new WinChecker(_yaku, Array.Empty<string>());
        _engine = new MahjongEngine(_points, _checker, _yaku, _random);
        Array.Clear(_uuids);
    }

    // ══════════════════════════════════════════════════════════════
    // 生命周期 / 设置
    // ══════════════════════════════════════════════════════════════

    /// <summary><c>init(config, checker, playerUUID, settings)</c> 的 C# 侧。</summary>
    public string Init(string settingsJson, string playerUUID)
    {
        _settings = ParseObject(settingsJson) ?? new JsonObject();
        _playerUUID = playerUUID ?? string.Empty;

        _uuids[0] = _playerUUID;
        var parts = _playerUUID.Split('-');
        for (int i = 0; i < 3; i++)
        {
            _uuids[i + 1] = parts.Length >= 3
                ? string.Join("-", parts[0], parts[1], parts[2], AiUuidLasts[i])
                : string.Empty;
        }

        _checker = new WinChecker(_yaku, OptionalYakuFromSettings());
        _engine = new MahjongEngine(_points, _checker, _yaku, _random);
        _engine.Players[0].IsHuman = true;
        _engine.Players[0].IsDealer = true;
        _engine.UpdateSeatWinds();

        _firstDeal = true;
        _diceDealer = null;
        _diceCut = null;
        _cutInfo = null;
        _modeTarget = 1;
        _speed = 250;
        _recordTiles = BoolOrNull(_settings, "recordTiles") ?? false;
        _allAI = false;
        _demoMode = false;
        ResetRoundWindows();
        return SnapshotJson();
    }

    /// <summary><c>applySettings(settings)</c>：记录设置 + 打开设置里的可选役。</summary>
    public string ApplySettings(string settingsJson)
    {
        var incoming = ParseObject(settingsJson);
        if (incoming is not null)
        {
            MergeInto(_settings, incoming);
        }

        _recordTiles = BoolOrNull(_settings, "recordTiles") ?? _recordTiles;
        EnableOptionalYaku(OptionalYakuFromSettings());
        return SnapshotJson();
    }

    /// <summary><c>setMode(target)</c>。</summary>
    public string SetMode(double target)
    {
        _modeTarget = Math.Max(1, (int)target);
        _engine.SetMode(_modeTarget);
        ResetRoundWindows();
        return SnapshotJson();
    }

    /// <summary><c>resetAll()</c>：回到未开局状态（保留可选役设置）。</summary>
    public string ResetAll()
    {
        _engine = new MahjongEngine(_points, _checker, _yaku, _random);
        _engine.SetMode(_modeTarget);
        _engine.Players[0].IsHuman = true;
        _engine.Players[0].IsDealer = true;
        _engine.UpdateSeatWinds();

        _firstDeal = true;
        _diceDealer = null;
        _diceCut = null;
        _cutInfo = null;
        _allAI = false;
        _demoMode = false;
        _speed = 250;
        ResetRoundWindows();
        return SnapshotJson();
    }

    /// <summary><c>applySavedState(state)</c>：会话恢复（字段与 engine.js 一致）。</summary>
    public bool ApplySavedState(string stateJson)
    {
        var state = ParseObject(stateJson);
        if (state is null || !BoolOrNull(state, "inProgress").GetValueOrDefault())
        {
            return false;
        }

        var uuid = Str(state, "playerUUID");
        if (uuid.Length > 0 && _playerUUID.Length > 0 && uuid != _playerUUID)
        {
            return false;
        }

        try
        {
            int modeTarget = (int)(LongOrNull(state, "modeTarget") ?? _modeTarget);
            _modeTarget = Math.Max(1, modeTarget);
            _engine.SetMode(_modeTarget);

            int handsPlayed = (int)(LongOrNull(state, "handsPlayed") ?? 0);
            int handNumber = (int)(LongOrNull(state, "handNumber") ?? 0);
            int dealerIndex = Math.Clamp((int)(LongOrNull(state, "dealerIndex") ?? 0), 0, MahjongEngine.PlayerCount - 1);
            int honba = (int)(LongOrNull(state, "honba") ?? 0);
            int roundWindIdx = (int)(LongOrNull(state, "roundWindIdx") ?? 0);
            int dealerCount = (int)(LongOrNull(state, "dealerCount") ?? 0);

            SetAutoProperty(_engine, "HandsPlayed", handsPlayed);
            _engine.HandNumber = handNumber;
            SetAutoProperty(_engine, "DealerIndex", dealerIndex);
            SetAutoProperty(_engine, "Honba", honba);
            SetAutoProperty(_engine, "RoundWindIdx", roundWindIdx);
            SetAutoProperty(_engine, "DealerCount", dealerCount);

            if (state["scores"] is JsonArray scores && scores.Count == MahjongEngine.PlayerCount)
            {
                var target = (int[])GetPrivateField(_engine, "_scores")!;
                for (int i = 0; i < target.Length; i++)
                {
                    target[i] = (int)DataStore.ToInt(scores[i]);
                }
            }

            _engine.UpdateSeatWinds();
            _firstDeal = false;
            ResetRoundWindows();
            Log("恢复存档: " + _engine.RoundLabel() + " / " + _modeTarget + "局");
            return true;
        }
        catch (Exception ex)
        {
            Log("恢复存档失败: " + ex.Message);
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 一局
    // ══════════════════════════════════════════════════════════════

    /// <summary><c>startNewRound()</c>：定庄（仅首局）/ 切牌 / 配牌，并摆好当前行动方。</summary>
    public string StartNewRound()
    {
        StartNewRoundInternal();
        return SnapshotJson();
    }

    private void StartNewRoundInternal()
    {
        ResetRoundWindows();

        int dealer = _engine.DealerIndex;
        if (_firstDeal)
        {
            _firstDeal = false;
            _diceDealer = TileSetFactory.RollDice(_random);
            dealer = TileSetFactory.DetermineDealer(_diceDealer);
        }

        // 与 engine.js 同序：先掷切牌骰，再洗牌/切牌/发牌（RoundDealFactory 内部完成）。
        var layout = RoundDealFactory.Cut(dealer, _random);
        _diceCut = layout.Dice;
        int sum = _diceCut[0] + _diceCut[1];
        int side = sum == 7
            ? dealer
            : sum < 7
                ? (dealer + sum) % MahjongEngine.PlayerCount
                : ((dealer - (sum % MahjongEngine.PlayerCount)) + MahjongEngine.PlayerCount) % MahjongEngine.PlayerCount;

        int skipTiles = layout.SkipTiles;
        _cutInfo = new JsonObject
        {
            ["side"] = side,
            ["stack"] = sum,
            ["skipTiles"] = skipTiles,
            ["maCount"] = MahjongEngine.DeadWallMaCount,
            ["wangCount"] = Math.Max(0, skipTiles - MahjongEngine.DeadWallMaCount),
        };

        _engine.StartNewRound(layout.Deal);
    }

    /// <summary>
    /// <c>advanceRound()</c> 的宿主版本（由 UI 的下一局定时器调用）：
    /// 推进局数 → 终局判定 → 开新局。
    /// </summary>
    public string AdvanceRound()
    {
        if (CurrentState != "ended" && !_engine.GameOver)
        {
            return SnapshotJson();
        }

        _stateOverride = null;
        ResetRoundWindows();
        if (_engine.AdvanceRound())
        {
            _engine.HandNumber++;
            StartNewRoundInternal();
        }
        else
        {
            _demoMode = false;
        }

        return SnapshotJson();
    }

    // ══════════════════════════════════════════════════════════════
    // AI 回合一拍
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 执行 AI 的一个完整回合（摸牌 → 自摸/杠 → 出牌 → 出牌后反应链），对应 <c>engine.js aiTurn</c>。
    /// JS 侧用 <c>setTimeout</c> 每拍调用一次，从而保留逐拍渲染。
    /// </summary>
    public string StepAi()
    {
        if (CurrentState != "ai_turn")
        {
            return SnapshotJson();
        }

        _stateOverride = null;
        AiTurn();
        return SnapshotJson();
    }

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
                _engine.Flow();
                return;
            }
        }
        else
        {
            player.FirstTurn = false;
        }

        var obs = SelfPlayDriver.BuildObservation(_engine, pi);
        var tsumo = AiPlayer.CheckTsumo(obs, _checker);
        if (tsumo.Success)
        {
            DeclareTsumo(pi, tsumo);
            return;
        }

        var kan = AiPlayer.DecideKan(obs);
        if (kan is not null)
        {
            if (DoKanWithRobCheck(player, kan.Tile, kan.Type))
            {
                var afterKan = SelfPlayDriver.BuildObservation(_engine, pi);
                var tsumo2 = AiPlayer.CheckTsumo(afterKan, _checker);
                if (tsumo2.Success)
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

        int discardIndex = AiPlayer.ChooseDiscardIndex(player.Hand, _random.NextDouble());
        if (discardIndex < 0 || discardIndex >= player.Hand.Count)
        {
            _engine.Flow();
            return;
        }

        var discarded = _engine.Discard(player, player.Hand[discardIndex]);
        if (discarded is null)
        {
            _engine.Flow();
            return;
        }

        AfterDiscard(discarded.Value, pi);
    }

    // ══════════════════════════════════════════════════════════════
    // 出牌后：特殊流局 / 荣和 / 副露
    // ══════════════════════════════════════════════════════════════

    private void AfterDiscard(Tile discarded, int discPlayer)
    {
        if (_engine.CheckSpecialFlow())
        {
            return;
        }

        int human = HumanIndex;
        if (human >= 0 && human != discPlayer)
        {
            var result = TryRon(human, discarded);
            if (result is not null)
            {
                _pendingRon = new PendingRonState(discarded, result, discPlayer);
                _stateOverride = "human_ron";
                _actOpts = new JsonObject { ["ron"] = true };
                _actMsg = "🎯 荣和机会! 别人打出 " + Display(discarded) + " · " + (result.TotalFu ?? 0) + "赋可和";
                _actLeft = ThinkSeconds;
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

    private List<RonCandidate> CheckRonForAi(Tile discarded, int discPlayer)
    {
        var obs = SelfPlayDriver.BuildObservation(_engine, -1, discPlayer, discarded);
        return AiPlayer.CheckRonDetailed(obs, _checker);
    }

    private void DeclareMultiRon(IReadOnlyList<RonCandidate> candidates, int discPlayer, Tile discarded)
    {
        var winners = new List<RonWinner>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var player = _engine.Players[candidate.PlayerIndex];
            // engine.js checkRon：荣和张留在胜者暗手里（快照/展示口径）。自走驱动同样这么加。
            player.Hand.Add(discarded);
            player.Hand.Sort();
            winners.Add(new RonWinner(candidate.PlayerIndex, SelfPlayDriver.ToSettlement(candidate.Result)));
        }

        _engine.DeclareMultiRon(winners, discPlayer);
    }

    private void CheckMelds(Tile discarded, int discPlayer)
    {
        var obs = SelfPlayDriver.BuildObservation(_engine, -1, discPlayer, discarded);
        var options = AiPlayer.GetMeldOptions(obs);

        int human = HumanIndex;
        if (human >= 0 && human != discPlayer)
        {
            bool canKan = options.Kan.Contains(human);
            bool canPon = options.Pon.Contains(human);
            bool canChi = options.Chi == human;
            if (canKan || canPon || canChi)
            {
                _pendingChiList = canChi
                    ? AiPlayer.ChiOptions(_engine.Players[human].Hand, discarded)
                    : new List<ChiOption>();
                _pendingMeld = new PendingMeldState(discarded, discPlayer);
                _stateOverride = "human_meld";
                _actOpts = new JsonObject
                {
                    ["kan"] = canKan,
                    ["pon"] = canPon,
                    ["chi"] = canChi,
                    ["chiList"] = ChiListJson(_pendingChiList),
                };
                _actMsg = "🀄 副露机会! 别人打出 " + Display(discarded);
                _actLeft = ThinkSeconds;
                return;
            }
        }

        var decision = AiPlayer.DecideMeld(obs, _random.NextDouble());
        if (!decision.Applied)
        {
            NextTurn();
            return;
        }

        _engine.ApplyMeld(
            decision.Target,
            decision.Type,
            discarded,
            discPlayer,
            decision.ChiTrio,
            _engine.PeekRinshanTile());
    }

    private void NextTurn()
    {
        _engine.NextTurn();
        if (_engine.GameOver)
        {
            return;
        }

        int human = HumanIndex;
        if (human < 0 || _engine.TurnIndex != human)
        {
            return;
        }

        if (_engine.State == EngineState.HumanDraw)
        {
            var drawn = _engine.DrawFromWall(_engine.Players[human]);
            if (drawn is null)
            {
                _engine.Flow();
                return;
            }

            _stateOverride = "human_discard";
        }
        else if (_engine.State == EngineState.AiTurn)
        {
            // 人类标志刚被 auto 切换回来时，引擎仍可能停在 AiTurn；直接交给出牌。
            _stateOverride = "human_discard";
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 荣和 / 杠
    // ══════════════════════════════════════════════════════════════

    /// <summary>人类荣和试探（engine.js <c>tryRon</c>）。成功时荣和张留在手里（与 JS 一致）。</summary>
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

        var context = BuildWinContext(playerIndex, player.Hand, selfDraw: false, tile);
        var result = _checker.CheckWin(
            player.Hand,
            selfDraw: false,
            hasCalled: player.Melds.Count > 0 || player.MinkanCount > 0,
            ankanCount: player.AnkanCount,
            minkanCount: player.MinkanCount,
            melds: ToCoreMelds(player.Melds),
            firstTurn: player.FirstTurn,
            winTile: tile,
            context: context);

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

    /// <summary>抢杠（engine.js <c>checkRobKan</c>）：加杠可被任意家抢；暗杠只有国士形能抢。</summary>
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

            var context = BuildWinContext(pi, player.Hand, selfDraw: false, tile);
            var result = _checker.CheckWin(
                player.Hand,
                selfDraw: false,
                hasCalled: player.Melds.Count > 0 || player.MinkanCount > 0,
                ankanCount: player.AnkanCount,
                minkanCount: player.MinkanCount,
                melds: ToCoreMelds(player.Melds),
                firstTurn: player.FirstTurn,
                winTile: tile,
                context: context);

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

    /// <summary>抢杠检查 + 执行杠。返回 true 表示杠成功（可继续判杠后自摸）。</summary>
    private bool DoKanWithRobCheck(Player player, Tile tile, MeldType type)
    {
        var robbers = CheckRobKan(tile, type, player.Index);
        if (robbers.Count > 0)
        {
            if (robbers.Count >= 3)
            {
                _engine.DeclareSpecialFlow("三家和牌（抢杠）→ 自动流局");
                return false;
            }

            var winners = robbers
                .Select(c => new RonWinner(c.PlayerIndex, SelfPlayDriver.ToSettlement(c.Result)))
                .ToList();
            _engine.DeclareMultiRon(winners, player.Index);
            return false;
        }

        return _engine.DoKan(player, tile, type, _engine.PeekRinshanTile());
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

    // ══════════════════════════════════════════════════════════════
    // 人类操作
    // ══════════════════════════════════════════════════════════════

    /// <summary><c>humanDiscard(idx)</c>。</summary>
    public string HumanDiscard(double index)
    {
        int human = HumanIndex;
        if (human < 0 || CurrentState != "human_discard" || _engine.TurnIndex != human)
        {
            return SnapshotJson();
        }

        var player = _engine.Players[human];
        int idx = (int)index;
        if (idx < 0 || idx >= player.Hand.Count)
        {
            return SnapshotJson();
        }

        _stateOverride = null;
        ClearAction();
        var discarded = _engine.Discard(player, player.Hand[idx]);
        if (discarded is null)
        {
            return SnapshotJson();
        }

        AfterDiscard(discarded.Value, human);
        return SnapshotJson();
    }

    /// <summary><c>humanChi(chiIndex)</c>。</summary>
    public string HumanChi(double chiIndex) => HumanMeldAction(MeldType.Chi, (int)chiIndex);

    /// <summary><c>humanPon()</c>。</summary>
    public string HumanPon() => HumanMeldAction(MeldType.Pon, 0);

    /// <summary><c>humanKan()</c>（明杠）。</summary>
    public string HumanKan() => HumanMeldAction(MeldType.Minkan, 0);

    private string HumanMeldAction(MeldType type, int chiIndex)
    {
        if (_pendingMeld is null)
        {
            return SnapshotJson();
        }

        int human = HumanIndex;
        var pending = _pendingMeld;
        _pendingMeld = null;
        _stateOverride = null;
        ClearAction();

        IReadOnlyList<Tile>? chiTrio = null;
        if (type == MeldType.Chi && _pendingChiList.Count > 0)
        {
            int idx = Math.Clamp(chiIndex, 0, _pendingChiList.Count - 1);
            chiTrio = _pendingChiList[idx].Trio;
        }

        _pendingChiList.Clear();
        _engine.ApplyMeld(
            human,
            type,
            pending.Tile,
            pending.Discarder,
            chiTrio,
            _engine.PeekRinshanTile());

        return SnapshotJson();
    }

    /// <summary><c>passAction()</c>：荣和 / 副露窗口的「过」。</summary>
    public string PassAction()
    {
        if (CurrentState == "human_ron")
        {
            PassRon();
        }
        else if (CurrentState == "human_meld")
        {
            PassMeld();
        }

        return SnapshotJson();
    }

    private void PassRon()
    {
        if (_pendingRon is null)
        {
            return;
        }

        int human = HumanIndex;
        var pending = _pendingRon;
        _pendingRon = null;
        _stateOverride = null;
        ClearAction();

        var player = _engine.Players[human];
        int pos = player.Hand.FindIndex(t => t == pending.Tile);
        if (pos >= 0)
        {
            player.Hand.RemoveAt(pos);
        }

        var candidates = CheckRonForAi(pending.Tile, pending.Discarder);
        if (candidates.Count > 0)
        {
            DeclareMultiRon(candidates, pending.Discarder, pending.Tile);
            return;
        }

        CheckMelds(pending.Tile, pending.Discarder);
    }

    private void PassMeld()
    {
        if (_pendingMeld is null)
        {
            return;
        }

        var pending = _pendingMeld;
        _pendingMeld = null;
        _pendingChiList.Clear();
        _stateOverride = null;
        ClearAction();

        var obs = SelfPlayDriver.BuildObservation(_engine, -1, pending.Discarder, pending.Tile);
        var decision = AiPlayer.DecideMeld(obs, _random.NextDouble());
        if (!decision.Applied)
        {
            NextTurn();
            return;
        }

        _engine.ApplyMeld(
            decision.Target,
            decision.Type,
            pending.Tile,
            pending.Discarder,
            decision.ChiTrio,
            _engine.PeekRinshanTile());
    }

    /// <summary><c>humanRon()</c>。</summary>
    public string HumanRon()
    {
        if (_pendingRon is null)
        {
            return SnapshotJson();
        }

        int human = HumanIndex;
        var pending = _pendingRon;
        _pendingRon = null;
        _stateOverride = null;
        ClearAction();

        var winners = new List<RonWinner>
        {
            new(human, SelfPlayDriver.ToSettlement(pending.Result)),
        };

        var aiWinners = CheckRonForAi(pending.Tile, pending.Discarder);
        foreach (var candidate in aiWinners)
        {
            var player = _engine.Players[candidate.PlayerIndex];
            player.Hand.Add(pending.Tile);
            player.Hand.Sort();
            winners.Add(new RonWinner(candidate.PlayerIndex, SelfPlayDriver.ToSettlement(candidate.Result)));
        }

        _engine.DeclareMultiRon(winners, pending.Discarder);
        return SnapshotJson();
    }

    /// <summary><c>humanKanSelf()</c>：暗杠 / 加杠。</summary>
    public string HumanKanSelf()
    {
        int human = HumanIndex;
        if (human < 0 || CurrentState != "human_discard")
        {
            return SnapshotJson();
        }

        var player = _engine.Players[human];
        var ankan = AiPlayer.CanAnkan(player.Hand);
        var kakan = ankan is null ? AiPlayer.CanKakan(ViewOf(player)) : null;
        var tile = ankan ?? kakan;
        if (tile is null)
        {
            return SnapshotJson();
        }

        var type = ankan is not null ? MeldType.Ankan : MeldType.Kakan;
        if (!DoKanWithRobCheck(player, tile.Value, type))
        {
            return SnapshotJson();
        }

        var obs = SelfPlayDriver.BuildObservation(_engine, human);
        var tsumo = AiPlayer.CheckTsumo(obs, _checker);
        if (tsumo.Success)
        {
            DeclareTsumo(human, tsumo);
        }

        return SnapshotJson();
    }

    /// <summary><c>humanTsumo()</c>。</summary>
    public string HumanTsumo()
    {
        int human = HumanIndex;
        if (human < 0 || CurrentState != "human_discard")
        {
            return SnapshotJson();
        }

        var player = _engine.Players[human];
        if (player.TotalTiles() != 14 + player.KongCount())
        {
            return SnapshotJson();
        }

        var obs = SelfPlayDriver.BuildObservation(_engine, human);
        var result = AiPlayer.CheckTsumo(obs, _checker);
        if (result.Success)
        {
            DeclareTsumo(human, result);
        }

        return SnapshotJson();
    }

    private void DeclareTsumo(int playerIndex, WinResult result)
    {
        _stateOverride = null;
        ClearAction();
        _engine.DeclareWin(playerIndex, selfDraw: true, SelfPlayDriver.ToSettlement(result));
    }

    /// <summary><c>humanFlow(reason)</c>：九种九牌 / 十三不靠 推牌流局。</summary>
    public string HumanFlow(string? reason)
    {
        int human = HumanIndex;
        if (human < 0 || CurrentState != "human_discard" || _engine.TurnIndex != human)
        {
            return SnapshotJson();
        }

        var options = _engine.FlowOptions(_engine.Players[human].Hand);
        if (options.Count == 0)
        {
            return SnapshotJson();
        }

        _stateOverride = null;
        ClearAction();
        string picked = !string.IsNullOrEmpty(reason) && options.Contains(reason) ? reason! : options[0];
        _engine.DeclarePushFlow(picked, human);
        return SnapshotJson();
    }

    // ══════════════════════════════════════════════════════════════
    // 命令行 / 其它
    // ══════════════════════════════════════════════════════════════

    /// <summary><c>executeCommand(cmd)</c>：debug.html / 键盘快捷键同口径。</summary>
    public string ExecuteCommand(string commandJson)
    {
        var command = ParseObject(commandJson);
        if (command is null)
        {
            return "noop";
        }

        switch (Str(command, "action"))
        {
            case "next":
                if (CurrentState == "ended" || _engine.GameOver)
                {
                    if (_engine.HandsPlayed >= _modeTarget)
                    {
                        ResetAll();
                        _engine.SetMode(_modeTarget);
                        _engine.HandNumber = 1;
                        StartNewRoundInternal();
                        return "next-new-game";
                    }

                    if (_engine.AdvanceRound())
                    {
                        _engine.HandNumber++;
                        StartNewRoundInternal();
                    }

                    return "next";
                }

                return "next-busy";

            case "auto":
            {
                _allAI = !_allAI;
                for (int i = 0; i < MahjongEngine.PlayerCount; i++)
                {
                    _engine.Players[i].IsHuman = !_allAI && i == 0;
                }

                if (_allAI)
                {
                    var speed = LongOrNull(command, "speed");
                    if (speed is > 0)
                    {
                        _speed = (int)speed.Value;
                    }

                    _demoMode = true;
                    _engine.HandNumber++;
                    StartNewRoundInternal();
                }
                else
                {
                    _demoMode = false;
                    StopAuto();
                }

                return "auto:" + (_allAI ? "true" : "false");
            }

            case "reset":
                ResetAll();
                return "reset";

            case "flow":
                HumanFlow(null);
                return "flow";

            case "setHand":
            {
                var parsed = ParseTileString(Str(command, "hand"));
                int human = HumanIndex;
                if (parsed is { Count: 14 } && human >= 0)
                {
                    var player = _engine.Players[human];
                    player.Hand.Clear();
                    player.Hand.AddRange(parsed);
                    player.Hand.Sort();
                    player.LastDraw = null;
                    _stateOverride = "human_discard";
                    return "setHand";
                }

                return "setHand-invalid";
            }

            case "speed":
                _speed = (int)(LongOrNull(command, "speed") ?? 0);
                return _speed > 0 ? "speed" : "speed-invalid";

            case "optional":
            {
                string id = Str(command, "id");
                if (id.Length > 0)
                {
                    SetOptional(id, BoolOrNull(command, "on").GetValueOrDefault());
                    return "optional";
                }

                return "optional-invalid";
            }

            case "record":
                _recordTiles = BoolOrNull(command, "on").GetValueOrDefault();
                return "record";

            case "render":
                return "render";

            default:
                return "unknown";
        }
    }

    private void StopAuto()
    {
        ResetRoundWindows();
        if (_engine.GameOver)
        {
            return;
        }

        int human = HumanIndex;
        if (human < 0)
        {
            return;
        }

        if (_engine.TurnIndex == human)
        {
            var player = _engine.Players[human];
            if (player.TotalTiles() == 13 + player.KongCount() && !player.SkipDraw)
            {
                var drawn = _engine.DrawFromWall(player);
                if (drawn is null)
                {
                    _engine.Flow();
                    return;
                }
            }

            _stateOverride = "human_discard";
        }
    }

    /// <summary>把 UI 直接改在镜像对象上的字段推回宿主（handNumber / speed / isHuman ...）。</summary>
    public string SyncClientState(string stateJson)
    {
        var state = ParseObject(stateJson);
        if (state is null)
        {
            return "ok";
        }

        if (LongOrNull(state, "handNumber") is { } handNumber)
        {
            _engine.HandNumber = (int)handNumber;
        }

        if (LongOrNull(state, "speed") is { } speed)
        {
            _speed = (int)speed;
        }

        if (BoolOrNull(state, "recordTiles") is { } recordTiles)
        {
            _recordTiles = recordTiles;
        }

        if (BoolOrNull(state, "demoMode") is { } demoMode)
        {
            _demoMode = demoMode;
        }

        if (BoolOrNull(state, "allAI") is { } allAI)
        {
            _allAI = allAI;
        }

        if (state["isHuman"] is JsonArray flags && flags.Count == MahjongEngine.PlayerCount)
        {
            for (int i = 0; i < MahjongEngine.PlayerCount; i++)
            {
                _engine.Players[i].IsHuman = DataStore.IsTruthy(flags[i]);
            }
        }

        return "ok";
    }

    public string SetSpeed(double speed)
    {
        _speed = (int)speed;
        return "ok";
    }

    public string SetRecordTiles(bool on)
    {
        _recordTiles = on;
        return "ok";
    }

    public string SetOptionalYaku(string id, bool on)
    {
        SetOptional(id, on);
        return "ok";
    }

    /// <summary>纯查询：当前手牌可用的推牌流局条件（engine.js <c>flowOptions</c>）。</summary>
    public string FlowOptions(string handJson)
    {
        var hand = ParseTiles(ParseArray(handJson));
        var options = _engine.FlowOptions(hand);
        var array = new JsonArray();
        foreach (var option in options)
        {
            array.Add(option);
        }

        return array.ToJsonString();
    }

    /// <summary>纯查询：<c>roundLabel()</c>。</summary>
    public string RoundLabel() => _engine.RoundLabel();

    /// <summary>纯查询：<c>calcPoints(fu)</c>。</summary>
    public double CalcPoints(double fu) => _engine.CalcPoints((int)fu);

    /// <summary>纯查询：<c>splitPayment(total)</c>。</summary>
    public string SplitPayment(double total)
    {
        var (each, remainder) = _engine.SplitPayment((int)total);
        return new JsonObject { ["each"] = each, ["remainder"] = remainder }.ToJsonString();
    }

    // ══════════════════════════════════════════════════════════════
    // 共用
    // ══════════════════════════════════════════════════════════════

    private string CurrentState => _stateOverride ?? _engine.State.Wire();

    private int HumanIndex
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

    private int ThinkSeconds => (int)(LongOrNull(_settings, "thinkSeconds") ?? 30);

    private void ResetRoundWindows()
    {
        _stateOverride = null;
        _pendingRon = null;
        _pendingMeld = null;
        _pendingChiList.Clear();
        ClearAction();
    }

    private void ClearAction()
    {
        _actOpts = null;
        _actMsg = string.Empty;
        _actLeft = 0;
    }

    private WinContext BuildWinContext(int playerIndex, IReadOnlyList<Tile> effectiveHand, bool selfDraw, Tile? winTile)
    {
        int visible = 0;
        if (winTile is { } target)
        {
            for (int pi = 0; pi < MahjongEngine.PlayerCount; pi++)
            {
                var player = _engine.Players[pi];
                visible += pi == playerIndex
                    ? Count(effectiveHand, target)
                    : Count(player.Hand, target);
                visible += Count(player.Discards, target);
                foreach (var meld in player.Melds)
                {
                    visible += Count(meld.Tiles, target);
                }
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

    private static List<CoreMeld> ToCoreMelds(IEnumerable<EngineMeld> melds) =>
        melds.Select(m => new CoreMeld(m.Type.Wire(), m.Tiles.ToList())).ToList();

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
            _ => tile.Num.ToString(),
        },
    };

    private static List<Tile>? ParseTileString(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var tiles = new List<Tile>();
        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }

            if (i == start || i >= text.Length)
            {
                return null;
            }

            char suitChar = char.ToLowerInvariant(text[i]);
            i++;
            var suit = suitChar switch
            {
                'm' => Suit.Man,
                'p' => Suit.Pin,
                's' => Suit.Sou,
                'z' => Suit.Honor,
                _ => (Suit)(-1),
            };

            if ((int)suit < 0)
            {
                return null;
            }

            for (int k = start; k < i - 1; k++)
            {
                int num = text[k] - '0';
                int max = suit == Suit.Honor ? 7 : 9;
                if (num < 1 || num > max)
                {
                    return null;
                }

                tiles.Add(new Tile(suit, num));
            }
        }

        return tiles.Count == 0 ? null : tiles;
    }

    // ══════════════════════════════════════════════════════════════
    // 设置 / 反射辅助（尽量不改动 Core / Engine 既有实现）
    // ══════════════════════════════════════════════════════════════

    private List<string> OptionalYakuFromSettings()
    {
        var ids = new List<string>();
        if (_settings["optionalYaku"] is JsonArray array)
        {
            foreach (var node in array)
            {
                var id = DataStore.NodeToString(node);
                if (id.Length > 0)
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private void EnableOptionalYaku(IEnumerable<string> ids)
    {
        var field = typeof(WinChecker).GetField("_optionalEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(_checker) is HashSet<string> set)
        {
            foreach (var id in ids)
            {
                set.Add(id);
            }
        }
    }

    private void SetOptional(string id, bool on)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var field = typeof(WinChecker).GetField("_optionalEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(_checker) is HashSet<string> set)
        {
            if (on)
            {
                set.Add(id);
            }
            else
            {
                set.Remove(id);
            }
        }
    }

    private static void SetAutoProperty(object target, string property, object value)
    {
        var field = target.GetType().GetField(
            $"<{property}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"找不到属性 {property} 的 backing field");
        }

        field.SetValue(target, value);
    }

    private static object? GetPrivateField(object target, string fieldName) =>
        target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

    private static void Log(string message) => Console.WriteLine("[engine] " + message);

    private sealed class SharedRandomSource : IRandomSource
    {
        public double NextDouble() => Random.Shared.NextDouble();
    }

    private sealed record PendingRonState(Tile Tile, WinResult Result, int Discarder);

    private sealed record PendingMeldState(Tile Tile, int Discarder);
}
