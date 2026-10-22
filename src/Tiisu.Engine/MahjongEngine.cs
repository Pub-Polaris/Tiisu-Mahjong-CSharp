using CoreMeld = Tiisu.Core.Meld;
using Tiisu.Core;
using Tiisu.Engine.Random;

namespace Tiisu.Engine;

/// <summary>
/// 七子麻将主状态机（Web 侧 <c>engine.js</c> 的移植）。回放语料把「谁摸到什么 / 谁打什么 / 谁副露 / 谁和」
/// 都记成事件，引擎只负责复现同样的状态影响与分数结算；不掷骰、不洗牌、不跑 AI 决策。
///
/// 与 JS 对齐的两个易错点：
/// <list type="bullet">
/// <item><see cref="AdvanceDealer"/> 只改 dealerIndex / 场风 / 门风，**不改 IsDealer 标志**。
///   所以局末快照里会看到"庄标记仍留在本局开局时的庄家"（语料就是这么记的）。</item>
/// <item>删除某张牌一律删「同 id 的第一张」——相同牌不可区分，与 JS 的 <c>indexOf/splice</c> 等价。</item>
/// </list>
/// 不引入计时器：JS 的 <c>setTimeout</c> 调度在回放里由语料事件驱动，<see cref="ScheduleNextRound"/> 只记录意图。
/// </summary>
public sealed partial class MahjongEngine
{
    public const int PlayerCount = 4;

    /// <summary>总牌数 136（无花牌）。</summary>
    public const int TotalTiles = 136;

    /// <summary>岭上（马）4 张。</summary>
    public const int DeadWallMaCount = 4;

    /// <summary>配牌张数：3 圈 × 4 家 × 4 张 + 每家 1 张 + 庄家 1 张。</summary>
    private const int DealTileCount = 53;

    private static readonly SeatWind[] WindOrder =
    {
        SeatWind.East, SeatWind.South, SeatWind.West, SeatWind.North,
    };

    private readonly Player[] _players = new Player[PlayerCount];
    private readonly int[] _scores = new int[PlayerCount];
    private readonly double[] _rankPoints = new double[PlayerCount];
    private readonly List<RoundDiscard> _roundDiscards = new();
    private readonly bool[] _westDiscarders = new bool[PlayerCount];
    private readonly List<Tile?> _wall = new();
    private readonly List<Tile?> _rinshan = new();
    private readonly List<Tile?> _wangDui = new();
    private readonly WinChecker _checker;
    private readonly YakuData _yakuData;
    private readonly PointsConfig _points;
    private List<RonWinner> _lastRonMulti = new();

    public MahjongEngine(
        PointsConfig? points = null,
        WinChecker? checker = null,
        YakuData? yakuData = null,
        IRandomSource? randomSource = null)
    {
        _points = points ?? PointsConfig.LoadDefault();
        _yakuData = yakuData ?? YakuData.LoadDefault();
        _checker = checker ?? new WinChecker(_yakuData);
        RandomSource = randomSource;
        for (int i = 0; i < PlayerCount; i++)
        {
            _players[i] = new Player(i, i switch
            {
                0 => "你",
                1 => "AI 南",
                2 => "AI 西",
                _ => "AI 北",
            });

            // 回放语料是全 AI 对局（engine.js 的 _allAI 模式）：四家都不走人类分支。
            _players[i].IsHuman = false;
        }
    }

    public IReadOnlyList<Player> Players => _players;

    /// <summary>自走注入的随机源（回放 / 普通构造为 null）。引擎自身不消费；由 <c>SelfPlayDriver</c> 取用。</summary>
    public IRandomSource? RandomSource { get; }

    /// <summary>判役器（自走时 AiPlayer 需要同一实例，避免重复加载 ike.json / 役表）。</summary>
    public WinChecker Checker => _checker;

    /// <summary>
    /// 牌山（含王堆，pop 顺序）。回放模式元素为 null 占位；传入真实 <c>WallOrder</c> 的自走模式为真实牌。
    /// </summary>
    public IReadOnlyList<Tile?> Wall => _wall;

    /// <summary>岭上（4 张）。回放模式元素为 null 占位；自走模式为真实牌。</summary>
    public IReadOnlyList<Tile?> Rinshan => _rinshan;

    /// <summary>王堆（skip−4 张）。回放模式元素为 null 占位；自走模式为真实牌。</summary>
    public IReadOnlyList<Tile?> WangDui => _wangDui;

    public IReadOnlyList<int> Scores => _scores;

    public IReadOnlyList<double> RankPoints => _rankPoints;

    public IReadOnlyList<RoundDiscard> RoundDiscards => _roundDiscards;

    public IReadOnlyList<RonWinner> LastRonMulti => _lastRonMulti;

    public int TurnIndex { get; private set; }

    public int DealerIndex { get; private set; }

    public int RoundWindIdx { get; private set; }

    public int Honba { get; private set; }

    public int HandNumber { get; set; }

    public int HandsPlayed { get; private set; }

    public int DealerCount { get; private set; }

    public int ModeTarget { get; private set; }

    /// <summary>场次初始点数（东风 / 半庄 49000；全庄 77000）。</summary>
    public int InitialPoints { get; private set; }

    public int FinalWindIdx { get; private set; }

    public EngineState State { get; private set; } = EngineState.Idle;

    public bool GameOver { get; private set; }

    public bool MatchOver { get; private set; }

    public bool JuFinished { get; private set; }

    public bool RevealHands { get; private set; }

    public bool KanThisTurn { get; private set; }

    public bool WeekEndHandled { get; private set; }

    public bool InExtension { get; private set; }

    public int WestDiscardCount { get; private set; }

    public int LastWestDiscarder { get; private set; } = -1;

    public SeatWind RoundWind => WindOrder[RoundWindIdx % 4];

    public Tile? LastDiscarded { get; private set; }

    public Tile? CurrentDrawn { get; private set; }

    /// <summary>JS 用 <c>setTimeout</c> 推进下一局；引擎不引入计时器，这里只记录"已安排下一局"。</summary>
    public int? PendingNextRoundDelay { get; private set; }

    /// <summary>模式 / 场次（engine.js <c>setMode</c>）：初始点数、场风、终局场风、段位点归零。</summary>
    public void SetMode(int modeTarget)
    {
        ModeTarget = modeTarget;
        HandsPlayed = 0;
        RoundWindIdx = 0;
        DealerCount = 0;
        InitialPoints = modeTarget >= 16 ? 77000 : 49000;
        for (int i = 0; i < PlayerCount; i++)
        {
            _scores[i] = InitialPoints;
            _rankPoints[i] = 0;
        }

        FinalWindIdx = modeTarget >= 16 ? 3 : modeTarget >= 8 ? 1 : 0;
        MatchOver = false;
        JuFinished = false;
        WeekEndHandled = false;
        InExtension = false;
    }

    /// <summary>
    /// 开新一局（engine.js <c>startNewRound</c>）。定庄骰、切牌骰、洗牌、配牌的具体结果由语料给出：
    /// 庄家、跳墩张数来自快照（<c>dealer</c> / <c>wang</c>），四家配牌来自快照的 <c>h</c>。
    /// 牌山仍按规则走一遍「132 张可摸 → 配牌扣 53 张」，于是 <c>wall=79</c>、<c>rinshan=4</c>、
    /// <c>wang=skip−4</c> 都由引擎自己算出来。
    /// </summary>
    public void StartNewRound(RoundDeal deal)
    {
        if (deal.Hands.Count != PlayerCount)
        {
            throw new ArgumentException("配牌必须是四家", nameof(deal));
        }

        DealerIndex = deal.DealerIndex;
        if (DealerIndex is < 0 or >= PlayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(deal), DealerIndex, "庄家序号越界");
        }

        RevealHands = false;
        foreach (var player in _players)
        {
            player.Reset();
        }

        SetupWallAndCut(deal.SkipTiles, deal.WallOrder);

        // deal()：牌山先被扣掉 53 张；具体配牌由语料给出（回放不洗牌、不发真牌）。
        if (_wall.Count < DealTileCount)
        {
            throw new InvalidOperationException($"牌山不足配牌张数：{_wall.Count} < {DealTileCount}（跳墩张数 {deal.SkipTiles} 过大）");
        }

        _wall.RemoveRange(_wall.Count - DealTileCount, DealTileCount);
        for (int i = 0; i < PlayerCount; i++)
        {
            _players[i].Hand.Clear();
            _players[i].Hand.AddRange(deal.Hands[i]);
            TileUtil.SortHand(_players[i].Hand);
        }

        State = EngineState.Playing;
        _roundDiscards.Clear();
        CurrentDrawn = null;
        GameOver = false;

        foreach (var player in _players)
        {
            player.IsDealer = false;
        }

        _players[DealerIndex].IsDealer = true;
        UpdateSeatWinds();
        TurnIndex = DealerIndex;

        // 本局重置：拔厄计数 / 杠标记 / 首巡听牌标志
        WestDiscardCount = 0;
        Array.Clear(_westDiscarders);
        LastWestDiscarder = -1;
        KanThisTurn = false;
        foreach (var player in _players)
        {
            player.FirstTurnTenpai = false;
        }

        State = _players[TurnIndex].IsHuman ? EngineState.HumanDiscard : EngineState.AiTurn;
    }

    /// <summary>
    /// 牌山模型（规则书 3.3 / 4.2 / 10.1）：跳墩 = 4 张岭上 + 王堆（skip−4）；
    /// 可摸总数 = 活牌山 + 王堆 = 136 − 4 = 132（配牌后恒为 79）。
    /// <para>
    /// <paramref name="wallOrder"/> 为空时保持回放模式：只按长度放 null 占位。
    /// 非空时按洗牌后的完整 136 张牌序切出真实岭上 / 王堆 / 活牌山（与 <c>setupWallAndCut</c> 同口径）。
    /// </para>
    /// </summary>
    public void SetupWallAndCut(int skipTiles, IReadOnlyList<Tile>? wallOrder = null)
    {
        int skip = Math.Max(0, skipTiles);
        int wangCount = Math.Max(0, skip - DeadWallMaCount);
        int liveCount = Math.Max(0, TotalTiles - skip);

        _rinshan.Clear();
        _wangDui.Clear();
        _wall.Clear();

        if (wallOrder is null)
        {
            for (int i = 0; i < DeadWallMaCount; i++)
            {
                _rinshan.Add(null);
            }

            for (int i = 0; i < wangCount; i++)
            {
                _wangDui.Add(null);
            }

            for (int i = 0; i < liveCount + wangCount; i++)
            {
                _wall.Add(null);
            }

            return;
        }

        var layout = TileSetFactory.Cut(wallOrder, skip);
        foreach (var tile in layout.Rinshan)
        {
            _rinshan.Add(tile);
        }

        foreach (var tile in layout.WangDui)
        {
            _wangDui.Add(tile);
        }

        foreach (var tile in layout.Wall)
        {
            _wall.Add(tile);
        }
    }

    /// <summary>门风轮转：庄家 = 场风，其后依次 南/西/北（摸牌顺序 0→1→2→3）。</summary>
    public void UpdateSeatWinds()
    {
        int rw = RoundWindIdx % 4;
        for (int i = 0; i < PlayerCount; i++)
        {
            int offset = (i - DealerIndex + 4) % 4;
            _players[i].SeatWind = WindOrder[(rw + offset) % 4];
        }
    }

    /// <summary>
    /// 庄家更替：庄家输 → 顺移到下一家；绕回起点时场风 +1。
    /// 注意：不更新 <see cref="Player.IsDealer"/> —— JS 只在 <see cref="StartNewRound"/> 里改这个标志。
    /// </summary>
    public void AdvanceDealer()
    {
        int next = (DealerIndex + 1) % PlayerCount;
        if (next == 0)
        {
            RoundWindIdx++;
        }

        DealerIndex = next;
        DealerCount++;
        Honba = 0;
        UpdateSeatWinds();
    }

    /// <summary>摸牌（engine.js <c>draw</c>）。回放：摸到的牌由语料给出，牌山只负责长度。</summary>
    public Tile? Draw(Player player, Tile tile)
    {
        if (_wall.Count == 0)
        {
            return null;
        }

        int before = _wall.Count;
        _wall.RemoveAt(_wall.Count - 1);
        ApplyDraw(player, tile, before);
        return tile;
    }

    /// <summary>
    /// 自走摸牌：从真实牌墙末尾取一张（不含岭上）。牌墙为回放占位（null）时抛异常，
    /// 提示调用方改用 <see cref="Draw(Player, Tile)"/> 或传 <c>WallOrder</c>。状态更新与 <see cref="Draw"/> 完全一致。
    /// </summary>
    public Tile? DrawFromWall(Player player)
    {
        if (_wall.Count == 0)
        {
            return null;
        }

        var tile = _wall[^1];
        if (tile is null)
        {
            throw new InvalidOperationException(
                "牌墙是回放占位（null），无法执行真实摸牌；请用 Draw(player, tile) 或传入 RoundDeal.WallOrder");
        }

        int before = _wall.Count;
        _wall.RemoveAt(_wall.Count - 1);
        ApplyDraw(player, tile.Value, before);
        return tile;
    }

    /// <summary>摸牌后的状态更新（<see cref="Draw"/> 与 <see cref="DrawFromWall"/> 共用）。</summary>
    private void ApplyDraw(Player player, Tile tile, int wallCountBefore)
    {
        player.Hand.Add(tile);
        TileUtil.SortHand(player.Hand);
        player.LastDraw = tile;
        player.SeaMoon = wallCountBefore == 2; // 倒数第二张墙牌自摸 → 海上月
        player.HaiDei = wallCountBefore == 1; // 最后一张墙牌自摸 → 海底月
        player.FirstTurn = false;
        CurrentDrawn = tile;
    }

    /// <summary>
    /// 自走用：下一次岭上补摸会取到的真实牌（不改变状态）：岭上有牌取岭上首张，否则取牌墙末尾（牌山代摸）。
    /// 回放的岭上 / 牌墙是 null 占位，返回 null，由调用方沿用既有 <c>rinshanTile</c> 传入方式。
    /// </summary>
    public Tile? PeekRinshanTile()
    {
        if (_rinshan.Count > 0)
        {
            return _rinshan[0];
        }

        return _wall.Count > 0 ? _wall[^1] : null;
    }

    /// <summary>
    /// 打出一张牌（engine.js <c>discard</c>）。回放只知道"打了哪张"，不知道下标：
    /// 相同牌不可区分，删第一张即可。
    /// </summary>
    public Tile? Discard(Player player, Tile tile)
    {
        int index = player.Hand.IndexOf(tile);
        if (index < 0)
        {
            return null;
        }

        var discarded = player.Hand[index];
        player.Hand.RemoveAt(index);
        player.Discards.Add(discarded);
        _roundDiscards.Add(new RoundDiscard(player.Index, discarded));
        LastDiscarded = discarded;

        // 首巡听牌标志（水滴石破）：本局第一张打出后，若剩 13 张已听牌 → 标记
        if (player.Discards.Count == 1)
        {
            player.FirstTurnTenpai = IsTenpai(player);
        }

        // 拔厄（4.3）：统计各家打出西风（z3）的家数
        if (discarded.Suit == Suit.Honor && discarded.Num == 3 && !_westDiscarders[player.Index])
        {
            _westDiscarders[player.Index] = true;
            WestDiscardCount++;
            LastWestDiscarder = player.Index;
        }

        // 出牌后 JS 会清掉本摸/免摸标记（AI 的 aiTurn 与人类 humanDiscard 都是这个净结果）
        player.LastDraw = null;
        player.SkipDraw = false;
        return discarded;
    }

    /// <summary>
    /// 出牌后的反应链入口（engine.js <c>afterDiscard</c>）。
    /// JS 顺序：特殊流局/拔厄 → 荣和 → 杠/碰/吃。回放模式下荣和/副露/下一家都由语料事件驱动，
    /// 这里只跑其中会**当场改分数**的特殊流局 / 拔厄，且时机与 JS 一致（在出牌快照之后）。
    /// </summary>
    public bool AfterDiscard() => CheckSpecialFlow();

    /// <summary>
    /// 特殊流局 / 拔厄检查（engine.js <c>checkSpecialFlow</c>）：
    /// 四风连打 → 当场流局并连庄（本场不变）；拔厄 → 最后打西风者向其他家各付 700，四家分数一致则本场 +1。
    /// 返回 true 表示已触发"流局"。
    /// </summary>
    public bool CheckSpecialFlow()
    {
        var discards = _roundDiscards;
        if (discards.Count >= 4)
        {
            var last4 = new List<RoundDiscard>(4);
            for (int i = discards.Count - 4; i < discards.Count; i++)
            {
                last4.Add(discards[i]);
            }

            var t0 = last4[0].Tile;
            bool isWind = TileUtil.IsWind(t0);
            bool same = last4.All(e => e.Tile == t0);
            bool fourPlayers = last4.Select(e => e.PlayerIndex).Distinct().Count() == PlayerCount;
            if (isWind && same && fourPlayers)
            {
                DeclareSpecialFlow("四风连打（" + TileUtil.Display(t0) + "）");
                return true;
            }
        }

        if (WestDiscardCount >= 4)
        {
            int payer = LastWestDiscarder;
            for (int k = 0; k < PlayerCount; k++)
            {
                if (k == payer)
                {
                    continue;
                }

                _scores[payer] -= 700;
                _scores[k] += 700;
            }

            bool allEqual = _scores.All(s => s == _scores[0]);
            if (allEqual)
            {
                Honba++;
            }

            WestDiscardCount = 0;
            Array.Clear(_westDiscarders);
        }

        return false;
    }

    /// <summary>特殊流局（四风连打 / 三家和牌 / 九种九牌等）：连庄、本场不变。</summary>
    public void DeclareSpecialFlow(string reason)
    {
        State = EngineState.Ended;
        GameOver = true;
        RevealHands = true;
        ScheduleNextRound(2000);
    }

    /// <summary>推牌流局（九种九牌 / 十三不靠）：连庄、本场不增加。</summary>
    public void DeclarePushFlow(string reason, int playerIndex)
    {
        State = EngineState.Ended;
        GameOver = true;
        RevealHands = true;
        ScheduleNextRound(2000);
    }

    /// <summary>兼容旧调用名。</summary>
    public void DeclareNineOrphans(int playerIndex) => DeclarePushFlow("九种九牌", playerIndex);

    /// <summary>
    /// 下一局调度。JS 用 <c>setTimeout</c>；引擎不引入计时器（也禁止引入），只记录意图，
    /// 由调用方（回放驱动 / 宿主）决定何时 <see cref="AdvanceRound"/> + <see cref="StartNewRound"/>。
    /// </summary>
    public void ScheduleNextRound(int delay) => PendingNextRoundDelay = delay;

    /// <summary>回合推进（engine.js <c>nextTurn</c>）。</summary>
    public void NextTurn()
    {
        KanThisTurn = false;
        foreach (var player in _players)
        {
            player.RinshanDrawn = false;
        }

        TurnIndex = (TurnIndex + 1) % PlayerCount;
        if (_wall.Count == 0)
        {
            Flow();
            return;
        }

        CurrentDrawn = null;
        LastDiscarded = null;

        var next = _players[TurnIndex];
        if (next.IsHuman)
        {
            State = next.TotalTiles() == 13 + next.KongCount() && !next.SkipDraw
                ? EngineState.HumanDraw
                : EngineState.HumanDiscard;
        }
        else
        {
            State = EngineState.AiTurn;
        }
    }

    /// <summary>
    /// 执行副露（engine.js <c>applyMeld</c>）。<paramref name="chiTrio"/> 是吃牌组合（语料给出）；
    /// <paramref name="rinshanTile"/> 是明杠的岭上补摸牌（语料快照推出）。抢杠判定属于判役性决策，
    /// 语料用 <c>kan</c> / <c>multi_ron</c> 事件直接给出结局，这里不重跑。
    /// </summary>
    public void ApplyMeld(
        int playerIndex,
        MeldType type,
        Tile tile,
        int discarder,
        IReadOnlyList<Tile>? chiTrio = null,
        Tile? rinshanTile = null)
    {
        var player = _players[playerIndex];
        switch (type)
        {
            case MeldType.Pon:
            case MeldType.Minkan:
            {
                int need = type == MeldType.Pon ? 2 : 3;
                RemoveTiles(player.Hand, tile, need);
                int total = type == MeldType.Pon ? 3 : 4;
                var tiles = new List<Tile>(total) { tile };
                for (int i = 1; i < total; i++)
                {
                    tiles.Add(tile);
                }

                player.AddMeld(type, tiles);
                break;
            }

            case MeldType.Chi:
            {
                var trio = chiTrio ?? ChiOptions(player, tile).FirstOrDefault()?.Trio;
                if (trio is null || trio.Count == 0)
                {
                    NextTurn();
                    return;
                }

                foreach (var member in trio)
                {
                    if (member == tile)
                    {
                        continue;
                    }

                    int pos = player.Hand.FindIndex(h => h == member);
                    if (pos >= 0)
                    {
                        player.Hand.RemoveAt(pos);
                    }
                }

                player.AddMeld(MeldType.Chi, trio.OrderBy(t => t.Num).ToList());
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "applyMeld 只接受 chi/pon/minkan");
        }

        TileUtil.SortHand(player.Hand);

        if (type == MeldType.Minkan)
        {
            // 明杠补摸岭上；岭上池耗尽则从活牌墙摸（不得流局）
            if (_wall.Count == 0 && _rinshan.Count == 0)
            {
                Flow();
                return;
            }

            DrawFromRinshan(player, rinshanTile ?? throw new InvalidOperationException("明杠需要岭上补摸的牌（回放由语料快照推出）"));
        }

        TurnIndex = playerIndex;
        LastDiscarded = null;
        player.SkipDraw = true; // 副露/杠后免常规摸牌（明杠已补摸岭上）
        State = player.IsHuman ? EngineState.HumanDiscard : EngineState.AiTurn;
    }

    /// <summary>
    /// 暗杠 / 加杠（engine.js <c>doKan</c>）。加杠时把碰升级成 kakan 并追加一张；
    /// 杠后从岭上补摸。抢杠判定同上：由语料的显式事件表达，这里不重跑。
    /// </summary>
    public bool DoKan(Player player, Tile tile, MeldType type, Tile? rinshanTile = null)
    {
        if (type is not (MeldType.Ankan or MeldType.Kakan))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "doKan 只接受 ankan/kakan");
        }

        RemoveTiles(player.Hand, tile, type == MeldType.Ankan ? 4 : 1);
        if (type == MeldType.Ankan)
        {
            player.AddMeld(MeldType.Ankan, new[] { tile, tile, tile, tile });
        }
        else
        {
            var pon = player.FindPonMeld(tile);
            if (pon is null)
            {
                return false;
            }

            pon.Type = MeldType.Kakan;
            pon.Tiles.Add(tile);
            player.MinkanCount++;
        }

        if (_wall.Count == 0 && _rinshan.Count == 0)
        {
            Flow();
            return false;
        }

        DrawFromRinshan(player, rinshanTile ?? throw new InvalidOperationException("杠后需要岭上补摸的牌（回放由语料快照推出）"));
        return true;
    }

    /// <summary>岭上补摸（engine.js 明杠 / 杠 里的那段）：先取岭上，岭上空了就从牌山代摸。</summary>
    private void DrawFromRinshan(Player player, Tile tile)
    {
        if (_rinshan.Count > 0)
        {
            _rinshan.RemoveAt(0);
        }
        else
        {
            _wall.RemoveAt(_wall.Count - 1);
        }

        KanThisTurn = true;
        player.RinshanDrawn = true;
        player.Hand.Add(tile);
        TileUtil.SortHand(player.Hand);
        player.LastDraw = tile;
        player.FirstTurn = false;
        CurrentDrawn = tile;
    }

    /// <summary>是否可吃（engine.js <c>canChi</c>）。</summary>
    public bool CanChi(Player player, Tile tile)
    {
        if (tile.IsHonor)
        {
            return false;
        }

        int n = tile.Num;
        bool Has(int x) => player.Hand.Any(t => t.Suit == tile.Suit && t.Num == x);
        if (n == 1)
        {
            return Has(2) && Has(3);
        }

        if (n == 9)
        {
            return Has(7) && Has(8);
        }

        if (Has(n - 1) && Has(n - 2))
        {
            return true;
        }

        if (Has(n - 1) && Has(n + 1))
        {
            return true;
        }

        return Has(n + 1) && Has(n + 2);
    }

    /// <summary>所有可行的吃牌组合（engine.js <c>chiOptions</c>）：如 12345 摸 4 → 234 / 345。</summary>
    public List<ChiOption> ChiOptions(Player player, Tile tile)
    {
        var options = new List<ChiOption>();
        if (tile.IsHonor)
        {
            return options;
        }

        int n = tile.Num;
        int Cnt(int x) => player.Hand.Count(t => t.Suit == tile.Suit && t.Num == x);

        var pairs = new List<(int Low, int High)>();
        if (n == 1)
        {
            if (Cnt(2) >= 1 && Cnt(3) >= 1)
            {
                pairs.Add((2, 3));
            }
        }
        else if (n == 9)
        {
            if (Cnt(7) >= 1 && Cnt(8) >= 1)
            {
                pairs.Add((7, 8));
            }
        }
        else
        {
            if (Cnt(n - 2) >= 1 && Cnt(n - 1) >= 1)
            {
                pairs.Add((n - 2, n - 1));
            }

            if (Cnt(n - 1) >= 1 && Cnt(n + 1) >= 1)
            {
                pairs.Add((n - 1, n + 1));
            }

            if (Cnt(n + 1) >= 1 && Cnt(n + 2) >= 1)
            {
                pairs.Add((n + 1, n + 2));
            }
        }

        foreach (var (low, high) in pairs)
        {
            var trio = new[] { tile, new Tile(tile.Suit, low), new Tile(tile.Suit, high) }
                .OrderBy(t => t.Num)
                .ToList();
            options.Add(new ChiOption(trio));
        }

        return options;
    }

    /// <summary>从手里删掉 count 张同 id 的牌（删最早出现的，与 JS filter/splice 等价）。</summary>
    private static void RemoveTiles(List<Tile> hand, Tile tile, int count)
    {
        int removed = 0;
        for (int i = 0; i < hand.Count && removed < count;)
        {
            if (hand[i] == tile)
            {
                hand.RemoveAt(i);
                removed++;
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>推牌流局条件（九种九牌 / 十三不靠）。</summary>
    public IReadOnlyList<string> FlowOptions(IReadOnlyList<Tile> hand)
    {
        var options = new List<string>();
        if (IsNineOrphans(hand))
        {
            options.Add("九种九牌");
        }

        if (IsThirteenUnrelated(hand))
        {
            options.Add("十三不靠");
        }

        return options;
    }

    /// <summary>人类「流局」按钮（engine.js <c>humanFlow</c>）。</summary>
    public void HumanFlow(string? reason)
    {
        int humanIndex = -1;
        for (int i = 0; i < PlayerCount; i++)
        {
            if (_players[i].IsHuman)
            {
                humanIndex = i;
                break;
            }
        }

        if (humanIndex < 0 || State != EngineState.HumanDiscard || TurnIndex != humanIndex)
        {
            return;
        }

        var options = FlowOptions(_players[humanIndex].Hand);
        if (options.Count == 0)
        {
            return;
        }

        var picked = reason is not null && options.Contains(reason) ? reason : options[0];
        DeclarePushFlow(picked, humanIndex);
    }

    /// <summary>九种九牌（engine.js <c>isNineOrphans</c>）。</summary>
    public bool IsNineOrphans(IReadOnlyList<Tile> hand)
    {
        var types = new HashSet<Tile>();
        foreach (var tile in hand)
        {
            if (tile.IsHonor || tile.Num is 1 or 9)
            {
                types.Add(tile);
            }
        }

        return types.Count >= 9;
    }

    /// <summary>十三不靠（engine.js <c>isThirteenUnrelated</c>）：三花色各一组 147/258/369 + 4 张不同字牌。</summary>
    public bool IsThirteenUnrelated(IReadOnlyList<Tile> hand)
    {
        if (hand.Count != 13)
        {
            return false;
        }

        var counts = TileUtil.Counts(hand);

        int honorCount = 0;
        for (int i = 27; i < 36; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            if (counts[i] != 1)
            {
                return false;
            }

            honorCount += counts[i];
        }

        if (honorCount != 4)
        {
            return false;
        }

        string[] progressions = { "147", "258", "369" };
        var used = new List<string>(3);
        for (int suit = 0; suit < 3; suit++)
        {
            var nums = new List<int>(3);
            for (int n = 1; n <= 9; n++)
            {
                int c = counts[(suit * 9) + n - 1];
                if (c > 1)
                {
                    return false;
                }

                if (c == 1)
                {
                    nums.Add(n);
                }
            }

            if (nums.Count != 3)
            {
                return false;
            }

            string key = string.Concat(nums);
            if (Array.IndexOf(progressions, key) < 0)
            {
                return false;
            }

            used.Add(key);
        }

        // 三种进度各用一次（万/饼/索 的分配可任意排列）
        return used.Distinct().Count() == 3;
    }

    /// <summary>本场标签（engine.js <c>roundLabel</c>）。</summary>
    public string RoundLabel()
    {
        string[] winds = { "东", "南", "西", "北" };
        int n = (DealerCount % 4) + 1;
        if (InExtension)
        {
            int w = RoundWindIdx % 4;
            return (w == 0 ? "北增" : winds[w] + "入") + n + "局";
        }

        return winds[RoundWindIdx % 4] + n + "局";
    }

    /// <summary>听牌判定（engine.js <c>isTenpai</c>：用 listTenpai 逐张试和）。</summary>
    public bool IsTenpai(Player player)
    {
        try
        {
            var entries = TenpaiCalculator.ListTenpai(_checker, player.Hand, ToCoreMelds(player.Melds), BuildTenpaiOptions(player));
            return entries.Any(x => x.Success);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>听牌列表（UI 听牌提示用）：只读包装，不改任何状态。</summary>
    public IReadOnlyList<TenpaiEntry> ListTenpaiFor(Player player) =>
        TenpaiCalculator.ListTenpai(_checker, player.Hand, ToCoreMelds(player.Melds), BuildTenpaiOptions(player));

    private TenpaiOptions BuildTenpaiOptions(Player player) => new()
    {
        SelfDraw = true,
        HasCalled = player.Melds.Count > 0 || player.MinkanCount > 0,
        AnkanCount = player.AnkanCount,
        MinkanCount = player.MinkanCount,
        FirstTurn = player.FirstTurn,
        SeatWind = player.SeatWind,
        RoundWind = RoundWind,
        BaseFu = _yakuData.BaseFu,
    };

    private static List<CoreMeld> ToCoreMelds(IEnumerable<Meld> melds) =>
        melds.Select(m => new CoreMeld(m.Type.Wire(), m.Tiles.ToList())).ToList();
}
