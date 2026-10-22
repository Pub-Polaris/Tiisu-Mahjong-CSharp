using System.Diagnostics;
using Tiisu.Engine.Random;

namespace Tiisu.Engine.Ai;

/// <summary>
/// 修罗档 MCTS 的预算（C 期，实验性）。
/// <see cref="MaxSimulations"/> 是候选**总数**上限（内部按候选均分），
/// <see cref="TimeBudget"/> 是兜底墙钟预算，超时提前停。
/// </summary>
public sealed record MctsOptions(int MaxSimulations, int MaxRolloutDraws, TimeSpan TimeBudget)
{
    /// <summary>
    /// 按自适应思考标记取预算：关键局面（NeedDeepThink）300 次模拟，普通局面 100 次，
    /// 兜底 200 ms（<see cref="AiTuning"/> 可调）。
    /// </summary>
    public static MctsOptions Adaptive(bool needDeepThink, AiTuning? tuning = null)
    {
        tuning ??= AiTuning.Default;
        return new MctsOptions(
            needDeepThink ? tuning.MctsDeepSimulations : tuning.MctsNormalSimulations,
            tuning.MctsMaxRolloutDraws,
            TimeSpan.FromMilliseconds(tuning.MctsTimeBudgetMs));
    }
}

/// <summary>一次简化 rollout 的结果：是否和牌、是否到过听牌、实际摸牌张数。</summary>
internal readonly record struct MctsRolloutResult(bool Win, bool Tenpai, int Draws);

/// <summary>
/// 修罗档 MCTS（C 期，实验性，默认关闭）。
///
/// <para>
/// 只使用公开信息：自家手牌 / 副露、四家牌河、别家副露、场况。模拟中的「摸牌」是从
/// **未见牌池抽样**（4 - 可见张数），不读取牌山，也不读取别家手牌。
/// 所有随机都走注入的 <see cref="IRandomSource"/>，同 seed 同决策（时间预算未耗尽时）。
/// </para>
/// <para>
/// 候选弃牌复用 <see cref="TieredAi"/> 困难档的评分排名，取前 K 个；每个候选跑
/// <c>MaxSimulations / K</c> 次简化 rollout，评分 =
/// 和牌率 × 期望赋 + 听牌率 × 系数 − 放铳危险（<see cref="ThreatModel.Danger"/>）。
/// 模拟次数为 0、预算非正或模拟异常时回退困难档。
/// </para>
/// </summary>
public static class MctsAi
{
    private const int StandardKindCount = 34;

    /// <summary>修罗档：返回弃牌索引（<paramref name="ctx"/> 里 Hand 的下标）。</summary>
    public static int ChooseDiscard(AiDecisionContext ctx, IRandomSource rng, MctsOptions options) =>
        ChooseDiscard(ctx, rng, options, hardRanking: null, tuning: null);

    /// <summary>
    /// 内部重载：<paramref name="hardRanking"/> 由 <see cref="TieredAi.Decide"/> 预先算好时直接复用，
    /// 避免修罗档重复算一遍困难档评分。
    /// </summary>
    internal static int ChooseDiscard(
        AiDecisionContext ctx,
        IRandomSource rng,
        MctsOptions options,
        IReadOnlyList<AiCandidate>? hardRanking,
        AiTuning? tuning)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(rng);
        tuning ??= AiTuning.Default;

        if (options is null
            || options.MaxSimulations <= 0
            || options.TimeBudget <= TimeSpan.Zero
            || ctx.Hand.Count == 0)
        {
            return Fallback(ctx, rng, tuning);
        }

        try
        {
            var ranking = hardRanking ?? TieredAi.RankCandidates(ctx, AiLevel.Hard, rng, tuning);
            return Search(ctx, rng, options, ranking, tuning);
        }
        catch
        {
            // 实验性功能：任何意外都不应影响对局，直接退回困难档。
            return Fallback(ctx, rng, tuning);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 搜索
    // ─────────────────────────────────────────────────────────────

    private static int Search(
        AiDecisionContext ctx,
        IRandomSource rng,
        MctsOptions options,
        IReadOnlyList<AiCandidate> ranking,
        AiTuning tuning)
    {
        if (ranking.Count == 0)
        {
            return Fallback(ctx, rng, tuning);
        }

        int limit = Math.Min(Math.Max(1, tuning.MctsCandidateLimit), ranking.Count);
        if (limit == 1)
        {
            // 只有一个候选：没有可比较的，直接就是它（单张手牌等退化情形）。
            return ranking[0].Index;
        }

        var rivers = TieredAi.Rivers(ctx);
        var threat = TieredAi.BuildThreatInput(ctx, rivers);
        var visible = Ukeire.CountVisible(ctx.Hand, ctx.Melds, rivers, ctx.OpponentMelds);
        var pool = BuildUnseen(visible);

        int perCandidate = Math.Max(1, options.MaxSimulations / limit);
        long deadline = Deadline(options.TimeBudget);

        // 每个候选「打出这张牌之后」的暗手（rollout 会改动它，所以每个模拟都从这里复制）。
        var starts = new List<Tile>[limit];
        for (int c = 0; c < limit; c++)
        {
            var discarded = new List<Tile>(ctx.Hand.Count - 1);
            for (int i = 0; i < ctx.Hand.Count; i++)
            {
                if (i != ranking[c].Index)
                {
                    discarded.Add(ctx.Hand[i]);
                }
            }

            starts[c] = discarded;
        }

        var wins = new int[limit];
        var tenpais = new int[limit];
        var simulations = new int[limit];

        // 轮转而不是逐个跑满：时间预算被裁掉时，各候选拿到的样本数依然接近，
        // 不会出现「前两个候选跑完、后面的候选一次都没跑」的偏差。
        for (int round = 0; round < perCandidate; round++)
        {
            if (Stopwatch.GetTimestamp() > deadline)
            {
                break;
            }

            bool anyThisRound = false;
            for (int c = 0; c < limit; c++)
            {
                if (Stopwatch.GetTimestamp() > deadline)
                {
                    break;
                }

                var unseen = new int[StandardKindCount];
                Array.Copy(pool, unseen, StandardKindCount);

                var result = RunRollout(
                    starts[c],
                    ctx.Melds,
                    unseen,
                    rng,
                    options.MaxRolloutDraws,
                    threat,
                    deadline,
                    tuning);

                simulations[c]++;
                anyThisRound = true;
                if (result.Win)
                {
                    wins[c]++;
                }

                if (result.Tenpai)
                {
                    tenpais[c]++;
                }
            }

            if (!anyThisRound)
            {
                break;
            }
        }

        int bestIndex = -1;
        double bestScore = double.NegativeInfinity;

        for (int c = 0; c < limit; c++)
        {
            if (simulations[c] == 0)
            {
                continue;
            }

            var candidate = ranking[c];
            double winRate = wins[c] / (double)simulations[c];
            double tenpaiRate = tenpais[c] / (double)simulations[c];
            double winValue = candidate.ExpectedValue > 0
                ? candidate.ExpectedValue
                : tuning.MctsBaseWinValue;

            double score = winRate * winValue * tuning.MctsWinWeight
                + tenpaiRate * tuning.MctsTenpaiWeight
                - candidate.Danger * tuning.MctsDangerWeight;

            // 严格大于：同分保留排名靠前（困难档评分更高）的候选，保证确定性。
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = candidate.Index;
            }
        }

        if (bestIndex < 0)
        {
            return Fallback(ctx, rng, tuning);
        }

        return bestIndex;
    }

    /// <summary>
    /// 一次简化 rollout：从当前暗手出发，反复「从未见牌池抽样摸牌 → 弃牌」，
    /// 直到和牌 / 摸满 <paramref name="maxDraws"/>。到过听牌即记 <see cref="MctsRolloutResult.Tenpai"/>。
    /// </summary>
    /// <param name="unseen">未见牌池（按 34 种牌计数），会被本次 rollout 就地扣减。</param>
    internal static MctsRolloutResult RunRollout(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        int[] unseen,
        IRandomSource rng,
        int maxDraws,
        ThreatInput threat,
        long deadlineTimestamp,
        AiTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(melds);
        ArgumentNullException.ThrowIfNull(unseen);
        ArgumentNullException.ThrowIfNull(rng);
        tuning ??= AiTuning.Default;

        var concealed = new List<Tile>(hand);
        var current = Shanten.Calculate(concealed, melds);
        if (current.Shanten < 0)
        {
            return new MctsRolloutResult(true, true, 0);
        }

        bool tenpai = current.Shanten <= 0;
        int draws = 0;

        for (int d = 0; d < maxDraws; d++)
        {
            if (Stopwatch.GetTimestamp() > deadlineTimestamp)
            {
                break;
            }

            if (!TryDraw(unseen, rng, out var drawn))
            {
                break;
            }

            concealed.Add(drawn);
            draws++;

            if (Shanten.Calculate(concealed, melds).Shanten < 0)
            {
                return new MctsRolloutResult(true, true, draws);
            }

            // 孤立的中张数牌不可能改善标准形 / 七对子，也不是国士候选 ——
            // 直接打回去，向听不变（省掉一轮 14 次向听计算，rollout 是热点）。
            if (IsUselessDraw(drawn, concealed))
            {
                concealed.RemoveAt(concealed.Count - 1);
                continue;
            }

            DiscardRolloutTile(concealed, melds, threat, rng, tuning);
            if (Shanten.Calculate(concealed, melds).Shanten <= 0)
            {
                tenpai = true;
            }
        }

        return new MctsRolloutResult(false, tenpai, draws);
    }

    /// <summary>
    /// 摸到的牌是否「留着没有任何用」：中张数牌（2–8，不可能是国士候选），且与手里任何一张
    /// 都不构成对子 / 搭子。这种牌打回去时向听严格不变，等价于「向听下降优先」的最优解。
    /// </summary>
    private static bool IsUselessDraw(Tile drawn, List<Tile> concealed)
    {
        if (drawn.IsHonor || drawn.IsTerminal)
        {
            return false;
        }

        // 最后一张就是刚摸到的牌。
        for (int i = 0; i < concealed.Count - 1; i++)
        {
            var other = concealed[i];
            if (other.Suit == drawn.Suit && Math.Abs(other.Num - drawn.Num) <= 2)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// rollout 内的弃牌：「向听下降优先」+ 少量随机（<see cref="AiTuning.MctsRolloutBlunder"/>）。
    /// 同向听时优先打危险度低的牌。
    /// </summary>
    private static void DiscardRolloutTile(
        List<Tile> concealed,
        IReadOnlyList<Meld> melds,
        ThreatInput threat,
        IRandomSource rng,
        AiTuning tuning)
    {
        int count = concealed.Count;
        if (count <= 1)
        {
            concealed.Clear();
            return;
        }

        if (rng.NextDouble() < tuning.MctsRolloutBlunder)
        {
            int pick = (int)(rng.NextDouble() * count);
            if (pick < 0)
            {
                pick = 0;
            }
            else if (pick >= count)
            {
                pick = count - 1;
            }

            concealed.RemoveAt(pick);
            return;
        }

        int bestIndex = -1;
        int bestShanten = int.MaxValue;
        double bestDanger = double.MaxValue;
        var seenKinds = new bool[36];

        for (int i = 0; i < count; i++)
        {
            var tile = concealed[i];
            if (seenKinds[tile.Index])
            {
                continue;
            }

            seenKinds[tile.Index] = true;

            // 就地移除再复原，避免每次评估都新建列表（rollout 是热点）。
            concealed.RemoveAt(i);
            int shanten = Shanten.Calculate(concealed, melds).Shanten;
            concealed.Insert(i, tile);

            if (shanten > bestShanten)
            {
                continue;
            }

            double danger = ThreatModel.Danger(tile, threat);
            if (shanten < bestShanten || danger < bestDanger)
            {
                bestShanten = shanten;
                bestDanger = danger;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
        {
            concealed.RemoveAt(bestIndex);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 抽样 / 预算
    // ─────────────────────────────────────────────────────────────

    /// <summary>未见牌池：每种牌 4 - 可见张数（自家手牌 / 副露 / 别家副露 / 四家牌河）。</summary>
    private static int[] BuildUnseen(int[] visible)
    {
        var unseen = new int[StandardKindCount];
        for (int i = 0; i < StandardKindCount; i++)
        {
            int count = 4 - visible[i];
            unseen[i] = count > 0 ? count : 0;
        }

        return unseen;
    }

    /// <summary>从未见牌池按张数加权抽样一张；池空返回 false。</summary>
    internal static bool TryDraw(int[] unseen, IRandomSource rng, out Tile tile)
    {
        int total = 0;
        for (int i = 0; i < StandardKindCount; i++)
        {
            total += unseen[i];
        }

        if (total <= 0)
        {
            tile = default;
            return false;
        }

        double roll = rng.NextDouble() * total;

        int cumulative = 0;
        for (int i = 0; i < StandardKindCount; i++)
        {
            if (unseen[i] <= 0)
            {
                continue;
            }

            cumulative += unseen[i];
            if (roll < cumulative)
            {
                unseen[i]--;
                tile = Tile.FromIndex(i);
                return true;
            }
        }

        // 浮点边界（roll 恰好落在 total 上）：退到最后一个非空牌种。
        for (int i = StandardKindCount - 1; i >= 0; i--)
        {
            if (unseen[i] > 0)
            {
                unseen[i]--;
                tile = Tile.FromIndex(i);
                return true;
            }
        }

        tile = default;
        return false;
    }

    private static long Deadline(TimeSpan budget)
    {
        double ticks = budget.TotalSeconds * Stopwatch.Frequency;
        if (double.IsNaN(ticks) || ticks >= long.MaxValue / 2.0)
        {
            return long.MaxValue;
        }

        return Stopwatch.GetTimestamp() + (long)ticks;
    }

    private static int Fallback(AiDecisionContext ctx, IRandomSource rng, AiTuning tuning) =>
        TieredAi.Decide(ctx, AiLevel.Hard, rng, tuning).Index;
}
