using Tiisu.Engine.Random;
using CoreMeld = Tiisu.Core.Meld;

namespace Tiisu.Engine.Ai;

/// <summary>四档 AI 难度。</summary>
public enum AiLevel
{
    Simple = 0,
    Normal = 1,
    Hard = 2,
    Shura = 3,
}

/// <summary>
/// 一次弃牌决策的只读输入。所有信息都必须是「该玩家可见」的：
/// 自家手牌 / 副露 / 牌河、四家牌河、别家副露、场况。
/// 不包含别家手牌或牌山。
/// </summary>
public sealed record AiDecisionContext(
    IReadOnlyList<Tile> Hand,
    IReadOnlyList<Meld> Melds,
    IReadOnlyList<Tile> Discards,
    IReadOnlyList<IReadOnlyList<Tile>> AllDiscards,
    IReadOnlyList<Meld> OpponentMelds,
    int Honba,
    int WallCount,
    int[] Scores,
    SeatWind SeatWind,
    SeatWind RoundWind,
    int AnkanCount,
    int MinkanCount)
{
    /// <summary>
    /// 决策者的绝对玩家序号（0..3）。未提供时 <see cref="MistakeModel"/> 回退为按
    /// <see cref="SeatWind"/> 取 <see cref="Scores"/> 下标（A 期约定的兼容口径）。
    /// </summary>
    public int PlayerIndex { get; init; } = -1;

    /// <summary>
    /// 四家副露（按座位顺序 0..3）。可选：提供时能按「某家副露数」精确计算威胁；
    /// 未提供时回退为「自家副露 + 其余别家副露合并到下一家」的兼容口径。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Meld>>? AllMelds { get; init; }

    /// <summary>外部显式提供的威胁模型输入；为 null 时由现有字段推导。</summary>
    public ThreatInput? Threat { get; init; }
}

/// <summary>一个候选打牌及其评分明细（供测试与上层诊断）。</summary>
public sealed record AiCandidate(
    int Index,
    Tile Tile,
    double Score,
    ShantenResult Shanten,
    int Ukeire,
    double ExpectedValue,
    bool Genbutsu,
    double KeepScore)
{
    /// <summary>这张牌的放铳危险度（0..1）；Simple 档与未启用防守的候选为 0。</summary>
    public double Danger { get; init; }
}

/// <summary>完整决策结果。<see cref="Index"/> 为最终选择，<see cref="Ranking"/> 按分数降序。</summary>
public sealed record AiDecision(
    int Index,
    AiLevel Level,
    bool NeedDeepThink,
    bool Mistake,
    IReadOnlyList<AiCandidate> Ranking);

/// <summary>
/// 分档弃牌（A 期，新 API）。
///
/// <list type="bullet">
/// <item>Simple：旧 <see cref="AiPlayer.ChooseDiscardIndex"/> 的启发式分值 + 噪声，不看进张。</item>
/// <item>Normal：进张数为主，平手用旧启发式；残局且自家向听仍远时叠加轻量防守。</item>
/// <item>Hard：进张 + 听牌后期望赋 + 现物安全 + 放铳危险度（<see cref="ThreatModel"/>）。</item>
/// <item>Shura：A 期等同 Hard（MCTS 在 C 期）。</item>
/// </list>
///
/// 随机源由调用方注入，同一 seed 同一输入得到同一决策。旧 API 不受影响。
/// </summary>
public static class TieredAi
{
    private static readonly Lazy<WinChecker> WinCheckerLazy = new(() => new WinChecker(YakuData.LoadDefault()));

    /// <summary>选弃牌（兼容简版调用）。</summary>
    public static int ChooseDiscard(AiDecisionContext ctx, AiLevel level, IRandomSource rng) =>
        Decide(ctx, level, rng).Index;

    /// <summary>完整决策（含 NeedDeepThink / 失误标记 / 候选排名）。</summary>
    public static AiDecision Decide(
        AiDecisionContext ctx,
        AiLevel level,
        IRandomSource rng,
        AiTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(rng);
        tuning ??= AiTuning.Default;

        if (ctx.Hand.Count == 0)
        {
            return new AiDecision(-1, level, false, false, Array.Empty<AiCandidate>());
        }

        var rivers = Rivers(ctx);
        var threat = BuildThreatInput(ctx, rivers);
        var keepScores = ComputeKeepScores(ctx.Hand);
        var candidates = new List<AiCandidate>(ctx.Hand.Count);

        // Normal 的防守是轻量、局部的：只在残局且自家向听仍远（≥2）时才生效。
        bool lateDefense = level == AiLevel.Normal
            && ctx.WallCount <= tuning.NormalDangerWallThreshold
            && Shanten.Calculate(ctx.Hand, ctx.Melds).Shanten >= tuning.NormalDangerShantenThreshold;
        bool useDanger = level is AiLevel.Hard or AiLevel.Shura || lateDefense;

        for (int i = 0; i < ctx.Hand.Count; i++)
        {
            var remaining = new List<Tile>(ctx.Hand.Count - 1);
            for (int j = 0; j < ctx.Hand.Count; j++)
            {
                if (j != i)
                {
                    remaining.Add(ctx.Hand[j]);
                }
            }

            var shanten = Shanten.Calculate(remaining, ctx.Melds);

            int ukeire = 0;
            double expectedValue = 0;
            bool genbutsu = IsGenbutsu(ctx.Hand[i], rivers);
            double danger = useDanger ? ThreatModel.Danger(ctx.Hand[i], threat) : 0.0;

            if (level != AiLevel.Simple)
            {
                var uke = Ukeire.Calculate(
                    remaining,
                    ctx.Melds,
                    rivers,
                    shanten,
                    ctx.OpponentMelds);
                ukeire = uke.TotalRemaining;

                if (level != AiLevel.Normal && shanten.Shanten == 0)
                {
                    expectedValue = EstimateTenpaiValue(remaining, ctx, rivers);
                }
            }

            double score = level switch
            {
                AiLevel.Simple => -keepScores[i] + Noise(rng, tuning.SimpleNoise),
                AiLevel.Normal => ukeire * tuning.NormalUkeireWeight
                    - keepScores[i] * tuning.NormalHeuristicTiebreak
                    - danger * tuning.NormalDangerWeight,
                _ => ukeire * tuning.HardUkeireWeight
                    + expectedValue * tuning.HardValueWeight
                    + (genbutsu ? tuning.HardGenbutsuWeight : 0.0)
                    - keepScores[i] * tuning.HardHeuristicTiebreak
                    - danger * tuning.HardDangerWeight,
            };

            candidates.Add(new AiCandidate(
                i,
                ctx.Hand[i],
                score,
                shanten,
                ukeire,
                expectedValue,
                genbutsu,
                keepScores[i])
            {
                Danger = danger,
            });
        }

        candidates.Sort((a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.Index.CompareTo(b.Index);
        });

        bool needDeepThink = ComputeNeedDeepThink(candidates, tuning);

        int selected = 0;
        bool mistake = false;
        if (candidates.Count > 1 && MistakeModel.ShouldMistake(ctx, level, rng, tuning))
        {
            selected = 1;
            mistake = true;
        }

        return new AiDecision(candidates[selected].Index, level, needDeepThink, mistake, candidates);
    }

    // ─────────────────────────────────────────────────────────────
    // 自适应思考标记
    // ─────────────────────────────────────────────────────────────

    private static bool ComputeNeedDeepThink(IReadOnlyList<AiCandidate> ranking, AiTuning tuning)
    {
        if (ranking.Count == 0)
        {
            return false;
        }

        if (ranking[0].Shanten.Shanten <= tuning.DeepThinkShantenThreshold)
        {
            return true;
        }

        if (ranking.Count < 2)
        {
            return false;
        }

        double top = ranking[0].Score;
        double second = ranking[1].Score;
        double denominator = Math.Max(1.0, Math.Max(Math.Abs(top), Math.Abs(second)));
        double gap = Math.Abs(top - second) / denominator;
        return gap < tuning.DeepThinkScoreGap;
    }

    // ─────────────────────────────────────────────────────────────
    // Hard：听牌后的粗略打点期望
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 用 <see cref="TenpaiCalculator"/>（只读包装 <see cref="WinChecker"/>）枚举和牌张，
    /// 按剩余张数加权平均「赋」。不修改判役逻辑。
    /// </summary>
    private static double EstimateTenpaiValue(
        IReadOnlyList<Tile> hand,
        AiDecisionContext ctx,
        IReadOnlyList<IReadOnlyList<Tile>> rivers)
    {
        var options = new TenpaiOptions
        {
            SelfDraw = false,
            HasCalled = ctx.Melds.Count > 0 || ctx.AnkanCount > 0 || ctx.MinkanCount > 0,
            AnkanCount = ctx.AnkanCount,
            MinkanCount = ctx.MinkanCount,
            FirstTurn = false,
            SeatWind = ctx.SeatWind,
            RoundWind = ctx.RoundWind,
        };

        List<TenpaiEntry> entries;
        try
        {
            entries = TenpaiCalculator.ListTenpai(
                WinCheckerLazy.Value,
                hand,
                ToCoreMelds(ctx.Melds),
                options);
        }
        catch
        {
            return 0.0;
        }

        if (entries.Count == 0)
        {
            return 0.0;
        }

        var visible = Ukeire.CountVisible(hand, ctx.Melds, rivers, ctx.OpponentMelds);
        double weighted = 0.0;
        int total = 0;
        foreach (var entry in entries)
        {
            if (!entry.Success)
            {
                continue;
            }

            int remaining = 4 - visible[entry.Tile.Index];
            if (remaining <= 0)
            {
                continue;
            }

            weighted += entry.TotalFu * remaining;
            total += remaining;
        }

        return total == 0 ? 0.0 : weighted / total;
    }

    // ─────────────────────────────────────────────────────────────
    // 可见信息 / 旧启发式
    // ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<IReadOnlyList<Tile>> Rivers(AiDecisionContext ctx) =>
        ctx.AllDiscards.Count > 0 ? ctx.AllDiscards : new[] { ctx.Discards };

    /// <summary>
    /// 从 <see cref="AiDecisionContext"/> 推导威胁输入。外部显式给了 <see cref="AiDecisionContext.Threat"/>
    /// 时直接使用；否则四家副露在缺少座位归属时退化为「自家 + 其余合并到下一家」。
    /// 只使用决策者可见的信息（牌河 / 副露 / 场况 / 自家手牌）。
    /// </summary>
    private static ThreatInput BuildThreatInput(
        AiDecisionContext ctx,
        IReadOnlyList<IReadOnlyList<Tile>> rivers)
    {
        if (ctx.Threat is not null)
        {
            return ctx.Threat;
        }

        int self = ctx.PlayerIndex is >= 0 and <= 3 ? ctx.PlayerIndex : (int)ctx.SeatWind;
        var allMelds = new List<IReadOnlyList<Meld>>(4);
        if (ctx.AllMelds is { Count: 4 } provided)
        {
            allMelds.AddRange(provided);
        }
        else
        {
            for (int seat = 0; seat < 4; seat++)
            {
                allMelds.Add(seat == self ? ctx.Melds : Array.Empty<Meld>());
            }

            if (ctx.OpponentMelds.Count > 0)
            {
                allMelds[(self + 1) % 4] = ctx.OpponentMelds;
            }
        }

        return new ThreatInput(
            rivers,
            allMelds,
            new[] { ctx.WallCount, ctx.WallCount, ctx.WallCount, ctx.WallCount },
            ctx.WallCount,
            ctx.Scores ?? Array.Empty<int>(),
            ctx.Honba,
            self)
        {
            SelfHand = ctx.Hand,
        };
    }

    private static bool IsGenbutsu(Tile tile, IReadOnlyList<IReadOnlyList<Tile>> rivers)
    {
        foreach (var river in rivers)
        {
            foreach (var discarded in river)
            {
                if (discarded == tile)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<CoreMeld> ToCoreMelds(IReadOnlyList<Meld> melds) =>
        melds.Select(m => new CoreMeld(m.Type.Wire(), m.Tiles.ToList())).ToList();

    private static double Noise(IRandomSource rng, double amplitude) =>
        (rng.NextDouble() * 2.0 - 1.0) * amplitude;

    /// <summary>
    /// 旧 <see cref="AiPlayer.ChooseDiscardIndex"/> 的「留牌分」：分越高越该留，最低分被切。
    /// 这里逐项复刻原公式，旧路径本身不改。
    /// </summary>
    private static double[] ComputeKeepScores(IReadOnlyList<Tile> hand)
    {
        int total = hand.Count;
        var suitStrength = new double[4];
        foreach (var tile in hand)
        {
            suitStrength[(int)tile.Suit] += 1;
        }

        for (int s = 0; s < suitStrength.Length; s++)
        {
            suitStrength[s] = suitStrength[s] / total * 100.0;
        }

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

            if (count >= 2)
            {
                score += 28;
            }

            if (count >= 3)
            {
                score += 38;
            }

            if (count >= 4)
            {
                score += 48;
            }

            score += suitStrength[(int)tile.Suit] * 0.05;

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

        return scores;
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
}
