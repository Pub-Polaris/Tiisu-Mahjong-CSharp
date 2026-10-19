using Tiisu.Core;
using CoreMeld = Tiisu.Core.Meld;

namespace Tiisu.Engine;

/// <summary>一位玩家在某个决策点的可见状态（从语料快照还原）。</summary>
public sealed class AiPlayerView
{
    public List<Tile> Hand { get; } = new();

    public List<Meld> Melds { get; } = new();

    public List<Tile> Discards { get; } = new();

    public SeatWind SeatWind { get; set; }

    public bool IsDealer { get; set; }

    public bool IsHuman { get; set; }

    public bool FirstTurn { get; set; } = true;

    public int AnkanCount { get; set; }

    public int MinkanCount { get; set; }

    public Tile? LastDraw { get; set; }

    public bool SeaMoon { get; set; }

    public bool HaiDei { get; set; }

    public bool RinshanDrawn { get; set; }

    public bool FirstTurnTenpai { get; set; }

    public bool SkipDraw { get; set; }
}

/// <summary>
/// 一个决策点的输入（只含状态，不含任何引擎实例 / 时钟 / 随机源）。
/// <see cref="PlayerIndex"/> 为决策者（discard/kan/tsumo）；meld/ron 是引擎级判定，记 -1。
/// </summary>
public sealed class AiPlayerObservation
{
    public int PlayerIndex { get; set; } = -1;

    public int TurnIndex { get; set; }

    public int DiscPlayer { get; set; } = -1;

    public Tile? Discarded { get; set; }

    public int WallCount { get; set; }

    public int RinshanCount { get; set; }

    public SeatWind RoundWind { get; set; }

    public int DealerIndex { get; set; }

    public int Honba { get; set; }

    public int HandNumber { get; set; }

    public int[] Scores { get; set; } = new int[MahjongEngine.PlayerCount];

    public Tile? LastDiscarded { get; set; }

    public bool KanThisTurn { get; set; }

    public List<AiPlayerView> Players { get; } = new();

    public List<string> OptionalYaku { get; } = new();
}

/// <summary>暗杠 / 加杠的决定（JS <c>canAnkan</c> / <c>canKakan</c> 的返回值）。</summary>
public sealed record AiKanDecision(Tile Tile, MeldType Type);

/// <summary>副露候选（JS <c>meldOptions</c>）。</summary>
public sealed class AiMeldOptions
{
    public List<int> Kan { get; } = new();

    public List<int> Pon { get; } = new();

    /// <summary>可以吃的人（庄家下家），null 表示不能吃。</summary>
    public int? Chi { get; set; }
}

/// <summary>副露决定：<see cref="Applied"/> 为 false 即 pass。</summary>
public sealed class AiMeldDecision
{
    public bool Applied { get; init; }

    public MeldType Type { get; init; }

    public int Target { get; init; } = -1;

    public IReadOnlyList<Tile>? ChiTrio { get; init; }

    public static AiMeldDecision Pass { get; } = new() { Applied = false };

    public static AiMeldDecision Meld(int target, MeldType type, IReadOnlyList<Tile>? chiTrio = null) =>
        new() { Applied = true, Target = target, Type = type, ChiTrio = chiTrio };
}

/// <summary>荣和候选：决策者 + 完整判役结果（自走结算需要赋数 / 役种，回放只需序号）。</summary>
public sealed record RonCandidate(int PlayerIndex, WinResult Result);

/// <summary>
/// AI 决策（Web 侧 <c>aiPlayer.js</c> 的 <c>getAIDiscardIndex</c> 与 <c>engine.js</c> 的
/// <c>aiTurn / canAnkan / canKakan / meldOptions / aiMeldDecision / checkRon</c> 的移植）。
///
/// 所有函数都是纯决策：输入只有 <see cref="AiPlayerObservation"/>（+ 本次决策消耗的随机值），
/// 不读 <c>Random</c>、<c>DateTime</c>、文件或引擎实例。判役复用 <see cref="WinChecker"/>。
/// </summary>
public static class AiPlayer
{
    // ─────────────────────────────────────────────────────────────
    // 弃牌
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// JS <c>getAIDiscardIndex(hand, game)</c>：按「面子 &gt; 搭子 &gt; 中心度」打分，取最低分；
    /// 全部同分时用 <paramref name="rnd"/>（对应 JS 那一次 <c>Math.random()</c>）随机选一张。
    /// </summary>
    public static int ChooseDiscardIndex(IReadOnlyList<Tile> hand, double rnd)
    {
        if (hand is null || hand.Count == 0)
        {
            return -1;
        }

        int total = hand.Count;

        // 1) 花色占比（极弱权重）
        var suitStrength = new double[4];
        foreach (var tile in hand)
        {
            suitStrength[(int)tile.Suit] += 1;
        }

        for (int s = 0; s < suitStrength.Length; s++)
        {
            suitStrength[s] = suitStrength[s] / total * 100.0;
        }

        // 2) 每张牌的张数
        var tileCount = new Dictionary<Tile, int>();
        foreach (var tile in hand)
        {
            tileCount.TryGetValue(tile, out int c);
            tileCount[tile] = c + 1;
        }

        var scores = new double[hand.Count];
        for (int i = 0; i < hand.Count; i++)
        {
            var tile = hand[i];
            double score = 0;
            int count = tileCount[tile];

            // 面子成型
            if (count >= 2) score += 28;
            if (count >= 3) score += 38;
            if (count >= 4) score += 48;

            // 花色集中度（微弱）
            score += suitStrength[(int)tile.Suit] * 0.05;

            // 顺子搭子
            if (HasNeighbor(tile, hand))
            {
                score += 20;
                score += NeighborCount(tile, hand) * 3;
            }

            if (tile.Suit == Suit.Honor)
            {
                score += count >= 2 ? 14 : -14;
            }
            else
            {
                int sameSuitCount = 0;
                foreach (var h in hand)
                {
                    if (h.Suit == tile.Suit)
                    {
                        sameSuitCount++;
                    }
                }

                if (sameSuitCount == 1 && count == 1)
                {
                    score += CenterBonus(tile.Num) - 14;
                }

                if (count == 1 && !HasNeighbor(tile, hand) && sameSuitCount > 1)
                {
                    score += CenterBonus(tile.Num);
                }
            }

            scores[i] = score;
        }

        double minScore = double.PositiveInfinity;
        int minIdx = 0;
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i] < minScore)
            {
                minScore = scores[i];
                minIdx = i;
            }
        }

        bool allEqual = true;
        foreach (double s in scores)
        {
            if (s != minScore)
            {
                allEqual = false;
                break;
            }
        }

        if (allEqual)
        {
            return (int)Math.Floor(rnd * hand.Count);
        }

        return minIdx;
    }

    private static bool HasNeighbor(Tile tile, IReadOnlyList<Tile> hand)
    {
        if (tile.Suit == Suit.Honor)
        {
            return false;
        }

        foreach (var h in hand)
        {
            if (h.Suit != tile.Suit)
            {
                continue;
            }

            int diff = Math.Abs(h.Num - tile.Num);
            if (diff is 1 or 2)
            {
                return true;
            }
        }

        return false;
    }

    private static int NeighborCount(Tile tile, IReadOnlyList<Tile> hand)
    {
        if (tile.Suit == Suit.Honor)
        {
            return 0;
        }

        int c = 0;
        int[] offsets = { -2, -1, 1, 2 };
        foreach (int offset in offsets)
        {
            int n = tile.Num + offset;
            if (n < 1 || n > 9)
            {
                continue;
            }

            foreach (var h in hand)
            {
                if (h.Suit == tile.Suit && h.Num == n)
                {
                    c++;
                    break;
                }
            }
        }

        return c;
    }

    private static int CenterBonus(int num) => num is >= 3 and <= 7 ? 8 : num is 2 or 8 ? 0 : -6;

    // ─────────────────────────────────────────────────────────────
    // 暗杠 / 加杠
    // ─────────────────────────────────────────────────────────────

    /// <summary>JS <c>aiTurn</c> 里的杠检查：暗杠优先，没有才考虑加杠。</summary>
    public static AiKanDecision? DecideKan(AiPlayerObservation obs)
    {
        var view = obs.Players[obs.PlayerIndex];
        if (CanAnkan(view.Hand) is Tile ankan)
        {
            return new AiKanDecision(ankan, MeldType.Ankan);
        }

        if (CanKakan(view) is Tile kakan)
        {
            return new AiKanDecision(kakan, MeldType.Kakan);
        }

        return null;
    }

    /// <summary>JS <c>canAnkan</c>：手里第一张凑满 4 张的牌（countTiles 的插入序 = 手牌顺序）。</summary>
    public static Tile? CanAnkan(IReadOnlyList<Tile> hand)
    {
        var counts = new Dictionary<Tile, int>();
        foreach (var tile in hand)
        {
            counts.TryGetValue(tile, out int c);
            counts[tile] = c + 1;
        }

        var seen = new HashSet<Tile>();
        foreach (var tile in hand)
        {
            if (!seen.Add(tile))
            {
                continue;
            }

            if (counts[tile] == 4)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>JS <c>canKakan</c>：手里有牌且已碰过同一张。</summary>
    public static Tile? CanKakan(AiPlayerView view)
    {
        var seen = new HashSet<Tile>();
        foreach (var tile in view.Hand)
        {
            if (!seen.Add(tile))
            {
                continue;
            }

            if (FindPonMeld(view.Melds, tile) is not null)
            {
                return tile;
            }
        }

        return null;
    }

    private static Meld? FindPonMeld(IReadOnlyList<Meld> melds, Tile tile)
    {
        foreach (var meld in melds)
        {
            if (meld.Type == MeldType.Pon && meld.Tiles.Count > 0 && meld.Tiles[0] == tile)
            {
                return meld;
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────
    // 副露
    // ─────────────────────────────────────────────────────────────

    /// <summary>JS <c>meldOptions</c>：谁可明杠 / 碰，下家能否吃。</summary>
    public static AiMeldOptions GetMeldOptions(AiPlayerObservation obs)
    {
        var opts = new AiMeldOptions();
        if (obs.Discarded is not Tile discarded)
        {
            return opts;
        }

        for (int pi = 0; pi < obs.Players.Count; pi++)
        {
            if (pi == obs.DiscPlayer)
            {
                continue;
            }

            int cnt = 0;
            foreach (var tile in obs.Players[pi].Hand)
            {
                if (tile == discarded)
                {
                    cnt++;
                }
            }

            if (cnt >= 3)
            {
                opts.Kan.Add(pi);
            }
            else if (cnt >= 2)
            {
                opts.Pon.Add(pi);
            }
        }

        int next = (obs.DiscPlayer + 1) % obs.Players.Count;
        if (CanChi(obs.Players[next].Hand, discarded))
        {
            opts.Chi = next;
        }

        return opts;
    }

    /// <summary>JS <c>canChi</c>。</summary>
    public static bool CanChi(IReadOnlyList<Tile> hand, Tile tile)
    {
        if (tile.Suit == Suit.Honor)
        {
            return false;
        }

        int n = tile.Num;
        bool Has(int x)
        {
            foreach (var t in hand)
            {
                if (t.Suit == tile.Suit && t.Num == x)
                {
                    return true;
                }
            }

            return false;
        }

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

    /// <summary>JS <c>chiOptions</c>：所有可行的吃牌组合，顺序与 JS 一致。</summary>
    public static List<ChiOption> ChiOptions(IReadOnlyList<Tile> hand, Tile tile)
    {
        var options = new List<ChiOption>();
        if (tile.Suit == Suit.Honor)
        {
            return options;
        }

        int n = tile.Num;
        int Cnt(int x)
        {
            int c = 0;
            foreach (var t in hand)
            {
                if (t.Suit == tile.Suit && t.Num == x)
                {
                    c++;
                }
            }

            return c;
        }

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
            var trio = new List<Tile> { tile, new Tile(tile.Suit, low), new Tile(tile.Suit, high) };
            trio.Sort((a, b) => a.Num.CompareTo(b.Num));
            options.Add(new ChiOption(trio));
        }

        return options;
    }

    /// <summary>
    /// JS <c>aiMeldDecision</c>：明杠 &gt; 碰 &gt; 吃；吃要求该家同花色 ≥5 张，
    /// 多组吃法时用 <paramref name="rnd"/> 随机选一组（对应 JS 那一次 <c>Math.random()</c>）。
    /// </summary>
    public static AiMeldDecision DecideMeld(AiPlayerObservation obs, double rnd)
    {
        if (obs.Discarded is not Tile discarded)
        {
            return AiMeldDecision.Pass;
        }

        var opts = GetMeldOptions(obs);
        int human = -1;
        for (int i = 0; i < obs.Players.Count; i++)
        {
            if (obs.Players[i].IsHuman)
            {
                human = i;
                break;
            }
        }

        int target = Pick(opts.Kan, human);
        MeldType type = MeldType.Minkan;
        IReadOnlyList<Tile>? chiTrio = null;

        if (target < 0)
        {
            target = Pick(opts.Pon, human);
            type = MeldType.Pon;
        }

        if (target < 0)
        {
            target = -1;
            if (opts.Chi is int chiSeat && chiSeat != human)
            {
                var np = obs.Players[chiSeat];
                int suitCnt = 0;
                foreach (var t in np.Hand)
                {
                    if (t.Suit == discarded.Suit)
                    {
                        suitCnt++;
                    }
                }

                if (suitCnt >= 5)
                {
                    var list = ChiOptions(np.Hand, discarded);
                    if (list.Count > 0)
                    {
                        int pick = (int)Math.Floor(rnd * list.Count);
                        target = chiSeat;
                        type = MeldType.Chi;
                        chiTrio = list[pick].Trio;
                    }
                }
            }
        }

        if (target < 0 || target == human)
        {
            return AiMeldDecision.Pass;
        }

        return AiMeldDecision.Meld(target, type, chiTrio);
    }

    private static int Pick(List<int> seats, int human)
    {
        foreach (int pi in seats)
        {
            if (pi != human)
            {
                return pi;
            }
        }

        return -1;
    }

    // ─────────────────────────────────────────────────────────────
    // 自摸 / 荣和
    // ─────────────────────────────────────────────────────────────

    /// <summary>JS <c>aiTurn</c> 里的自摸判定（暗手已含本摸）。</summary>
    public static WinResult CheckTsumo(AiPlayerObservation obs, WinChecker checker)
    {
        var view = obs.Players[obs.PlayerIndex];
        var melds = ToCoreMelds(view.Melds);
        var ctx = BuildWinContext(obs, obs.PlayerIndex, view.Hand, selfDraw: true, view.LastDraw);
        return checker.CheckWin(
            view.Hand.ToList(),
            true,
            view.Melds.Count > 0 || view.MinkanCount > 0,
            view.AnkanCount,
            view.MinkanCount,
            melds,
            view.FirstTurn,
            view.LastDraw,
            ctx);
    }

    /// <summary>
    /// JS <c>checkRon(discardedTile)</c>：把牌临时加进每家暗手试和，返回能荣和的家（升序）。
    /// 人类（<see cref="AiPlayerView.IsHuman"/>）跳过——与 JS 一致。
    /// </summary>
    public static List<int> CheckRon(AiPlayerObservation obs, WinChecker checker) =>
        CheckRonDetailed(obs, checker).Select(candidate => candidate.PlayerIndex).ToList();

    /// <summary>
    /// 同 <see cref="CheckRon"/>，但保留每位荣和者的完整判役结果（自走结算用）。
    /// 顺序 / 防御性张数检查与 JS <c>checkRon</c> 逐条一致，<see cref="CheckRon"/> 只是它的投影。
    /// </summary>
    public static List<RonCandidate> CheckRonDetailed(AiPlayerObservation obs, WinChecker checker)
    {
        var winners = new List<RonCandidate>();
        if (obs.Discarded is not Tile discarded)
        {
            return winners;
        }

        for (int pi = 0; pi < obs.Players.Count; pi++)
        {
            if (pi == obs.TurnIndex)
            {
                continue;
            }

            var view = obs.Players[pi];
            if (view.IsHuman)
            {
                continue;
            }

            // 防御：荣和前必须为 13 张（含副露后的暗手），加上荣和张 = 14 张。
            int meldTiles = 0;
            int kongCount = 0;
            foreach (var meld in view.Melds)
            {
                meldTiles += meld.Tiles.Count;
                if (meld.Tiles.Count == 4)
                {
                    kongCount++;
                }
            }

            int expectedConcealed = 13 - meldTiles + kongCount;
            if (view.Hand.Count != expectedConcealed)
            {
                continue;
            }

            var trial = new List<Tile>(view.Hand) { discarded };
            var melds = ToCoreMelds(view.Melds);
            var ctx = BuildWinContext(obs, pi, trial, selfDraw: false, discarded);
            var result = checker.CheckWin(
                trial,
                false,
                view.Melds.Count > 0 || view.MinkanCount > 0,
                view.AnkanCount,
                view.MinkanCount,
                melds,
                view.FirstTurn,
                discarded,
                ctx);

            if (result.Success)
            {
                winners.Add(new RonCandidate(pi, result));
            }
        }

        return winners;
    }

    private static List<CoreMeld> ToCoreMelds(IEnumerable<Meld> melds) =>
        melds.Select(m => new CoreMeld(m.Type.Wire(), m.Tiles.ToList())).ToList();

    /// <summary>JS <c>buildWinCtx</c>（偶发役上下文），字段口径逐项对齐。</summary>
    private static WinContext BuildWinContext(
        AiPlayerObservation obs,
        int playerIndex,
        IReadOnlyList<Tile> effectiveHand,
        bool selfDraw,
        Tile? winTile)
    {
        var self = obs.Players[playerIndex];

        int visible = 0;
        if (winTile is Tile wt)
        {
            for (int pi = 0; pi < obs.Players.Count; pi++)
            {
                var view = obs.Players[pi];
                if (pi == playerIndex)
                {
                    visible += Count(effectiveHand, wt);
                }
                else
                {
                    visible += Count(view.Hand, wt);
                }

                visible += Count(view.Discards, wt);
                foreach (var meld in view.Melds)
                {
                    visible += Count(meld.Tiles, wt);
                }
            }
        }

        bool anyCalled = false;
        foreach (var view in obs.Players)
        {
            if (view.Melds.Count > 0 || view.MinkanCount > 0)
            {
                anyCalled = true;
                break;
            }
        }

        bool renhe = !selfDraw && !self.IsDealer && !anyCalled && self.FirstTurn && self.LastDraw is null;
        bool seaMoon = selfDraw && self.SeaMoon;
        bool haiDei = selfDraw && self.HaiDei;
        bool riverFish = !selfDraw && obs.WallCount == 0;
        bool rinshanFlower = selfDraw && self.RinshanDrawn;
        bool rinshanGun = !selfDraw && obs.KanThisTurn;
        bool waterDrop = self.FirstTurnTenpai && (selfDraw ? self.HaiDei : obs.WallCount == 0);

        return new WinContext
        {
            SeaMoon = seaMoon,
            HaiDei = haiDei,
            RiverFish = riverFish,
            LastTileVisible = visible,
            SeatWind = self.SeatWind.Wire(),
            RoundWind = obs.RoundWind.Wire(),
            WaterDrop = waterDrop,
            RinshanGun = rinshanGun,
            RinshanFlower = rinshanFlower,
            KanRiichi = obs.KanThisTurn,
            Renhe = renhe,
        };
    }

    private static int Count(IReadOnlyList<Tile> tiles, Tile target)
    {
        int c = 0;
        foreach (var tile in tiles)
        {
            if (tile == target)
            {
                c++;
            }
        }

        return c;
    }
}
