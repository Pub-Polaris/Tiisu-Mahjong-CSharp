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

    /// <summary>是否首巡（影响人和 / 首巡听牌口径，报听判定用）。</summary>
    public bool FirstTurn { get; init; }

    /// <summary>
    /// 是否启用修罗档 MCTS（C 期，实验性，默认关闭）。
    /// 只影响 <see cref="AiLevel.Shura"/>：为 false 时修罗完全等价困难档，行为与 A/B 期一致。
    /// 由设置 <c>AiMctsEnabled</c> 传入（也可用 <see cref="TieredAi.Decide"/> 的参数覆盖）。
    /// </summary>
    public bool MctsEnabled { get; init; }
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
    IReadOnlyList<AiCandidate> Ranking)
{
    /// <summary>本次决策是否真的走了 MCTS（仅修罗档且启用时可能为 true，供诊断 / 测试）。</summary>
    public bool Mcts { get; init; }
}

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

    /// <summary>
    /// 完整决策（含 NeedDeepThink / 失误标记 / 候选排名）。
    /// </summary>
    /// <param name="mctsEnabled">
    /// 修罗档 MCTS 开关；为 null 时取 <see cref="AiDecisionContext.MctsEnabled"/>。
    /// 只影响 <see cref="AiLevel.Shura"/>，其余档位不受影响。
    /// </param>
    public static AiDecision Decide(
        AiDecisionContext ctx,
        AiLevel level,
        IRandomSource rng,
        AiTuning? tuning = null,
        bool? mctsEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(rng);
        tuning ??= AiTuning.Default;

        if (ctx.Hand.Count == 0)
        {
            return new AiDecision(-1, level, false, false, Array.Empty<AiCandidate>());
        }

        var candidates = RankCandidates(ctx, level, rng, tuning);
        bool needDeepThink = ComputeNeedDeepThink(candidates, tuning);

        if (level == AiLevel.Shura && (mctsEnabled ?? ctx.MctsEnabled))
        {
            var options = MctsOptions.Adaptive(needDeepThink, tuning);
            int mctsIndex = MctsAi.ChooseDiscard(ctx, rng, options, candidates, tuning);
            return new AiDecision(mctsIndex, level, needDeepThink, false, candidates) { Mcts = true };
        }

        int selected = 0;
        bool mistake = false;
        if (candidates.Count > 1 && MistakeModel.ShouldMistake(ctx, level, rng, tuning))
        {
            selected = 1;
            mistake = true;
        }

        return new AiDecision(candidates[selected].Index, level, needDeepThink, mistake, candidates);
    }

    /// <summary>
    /// 只算候选排名（不掷失误、不跑 MCTS）。修罗档 MCTS 复用这里的困难档评分取前 K 个候选。
    /// </summary>
    internal static IReadOnlyList<AiCandidate> RankCandidates(
        AiDecisionContext ctx,
        AiLevel level,
        IRandomSource rng,
        AiTuning tuning)
    {
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
                AiLevel.Simple => -shanten.Shanten * 10_000.0
                    + keepScores[i] * tuning.SimpleIsolationWeight
                    + Noise(rng, tuning.SimpleNoise),
                AiLevel.Normal => -shanten.Shanten * 10_000.0
                    + ukeire * tuning.NormalUkeireWeight
                    + keepScores[i] * tuning.NormalIsolationWeight
                    - danger * tuning.NormalDangerWeight,
                _ => -shanten.Shanten * 10_000.0
                    + ukeire * tuning.HardUkeireWeight
                    + expectedValue * tuning.HardValueWeight
                    + (genbutsu ? tuning.HardGenbutsuWeight : 0.0)
                    + keepScores[i] * tuning.HardIsolationWeight
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

        return candidates;
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

    internal static IReadOnlyList<IReadOnlyList<Tile>> Rivers(AiDecisionContext ctx) =>
        ctx.AllDiscards.Count > 0 ? ctx.AllDiscards : new[] { ctx.Discards };

    /// <summary>
    /// 从 <see cref="AiDecisionContext"/> 推导威胁输入。外部显式给了 <see cref="AiDecisionContext.Threat"/>
    /// 时直接使用；否则四家副露在缺少座位归属时退化为「自家 + 其余合并到下一家」。
    /// 只使用决策者可见的信息（牌河 / 副露 / 场况 / 自家手牌）。
    /// </summary>
    internal static ThreatInput BuildThreatInput(
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
    /// 弃牌优先级分（0..100，越高越该先打），cleanroom 规则：
    /// 孤张字牌(100) → 孤张幺九(85) → 孤张中张(60) → 嵌张(40) → 两面/边张(25) → 两面+嵌张(15) → 面子/对子(0)。
    /// 所有档位共用同一套优先级，保证 AI 先处理孤张，不会为了避危险拆已成形的面子。
    /// </summary>
    private static double[] ComputeKeepScores(IReadOnlyList<Tile> hand)
    {
        var counts = new Dictionary<Tile, int>();
        foreach (var tile in hand)
        {
            counts.TryGetValue(tile, out int c);
            counts[tile] = c + 1;
        }

        var scores = new double[hand.Count];
        for (int i = 0; i < hand.Count; i++)
        {
            var tile = hand[i];
            if (counts[tile] >= 2)
            {
                scores[i] = 0;   // 对子 / 刻子：最后才拆
                continue;
            }

            if (tile.Suit == Suit.Honor)
            {
                scores[i] = 100; // 孤张字牌：最先打
                continue;
            }

            bool hasAdjacent = false;
            bool hasGap = false;
            foreach (var other in hand)
            {
                if (other.Suit != tile.Suit)
                {
                    continue;
                }

                int diff = Math.Abs(other.Num - tile.Num);
                if (diff == 1)
                {
                    hasAdjacent = true;
                }
                else if (diff == 2)
                {
                    hasGap = true;
                }
            }

            scores[i] = hasAdjacent && hasGap ? 15
                : hasAdjacent ? 25
                : hasGap ? 40
                : tile.Num is 1 or 9 ? 85 : 60;
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
