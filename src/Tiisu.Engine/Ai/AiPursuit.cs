using System;
using System.Collections.Generic;

namespace Tiisu.Engine.Ai;

/// <summary>
/// 追役目标（七子麻将里可以主动做的稀有役 / 高役）。
/// 只做原型：识别候选、给弃牌加目标进度 bonus、约束副露 / 报听，不重训策略网。
/// </summary>
public enum PursuitTarget
{
    /// <summary>未处于追役状态。</summary>
    None = 0,

    /// <summary>七对子（门清；四张算两对，与 <c>WinChecker.IsSevenPairs</c> 同口径）。</summary>
    SevenPairs,

    /// <summary>混一色 / 清一色（允许副露）。</summary>
    Flush,

    /// <summary>字一色（允许副露；这是「追役少副露」规则的唯一例外）。</summary>
    AllHonors,

    /// <summary>大七星：七种字牌各一对（必须门清）。</summary>
    BigSevenStars,

    /// <summary>九莲宝灯（必须门清）。</summary>
    NineGates,

    /// <summary>四暗杠（必须全暗杠，门清）。</summary>
    FourAnkan,
}

/// <summary>
/// 一次追役判定/快照。由 <see cref="AiPursuit.Observe"/> 产出，只读。
/// </summary>
/// <param name="Target">当前目标；<see cref="PursuitTarget.None"/> = 不在追役。</param>
/// <param name="Active">是否处于追役状态（普通弃牌仍是 argmax；只有 true 时才温度采样）。</param>
/// <param name="Progress">当前目标进度 0..1。</param>
/// <param name="BestProgress">进入追役以来的最好进度（用于检测恶化退出）。</param>
/// <param name="Temperature">追役状态弃牌采样温度（&lt;1 更贪心，&gt;1 更随机）。</param>
/// <param name="Suit">目标花色 0..2（清一色 / 九莲）；其余目标为 -1。</param>
public readonly record struct PursuitState(
    PursuitTarget Target,
    bool Active,
    double Progress,
    double BestProgress,
    double Temperature,
    int Suit)
{
    /// <summary>未追役。</summary>
    public static PursuitState Inactive { get; } = new(PursuitTarget.None, false, 0.0, 0.0, 1.0, -1);

    /// <summary>该目标是否要求门清（七对 / 大七星 / 九莲 / 四暗杠）。</summary>
    public bool RequiresClosed =>
        Target is PursuitTarget.SevenPairs
            or PursuitTarget.BigSevenStars
            or PursuitTarget.NineGates
            or PursuitTarget.FourAnkan;
}

/// <summary>
/// 7Neuu 自动追役原型（A 期）。
///
/// <para>
/// 每个 AI 座位持有一个 <see cref="AiPursuit"/> 实例：<see cref="Observe"/> 根据当前可见手牌
/// 自动判断是否进入 / 退出追役，并产出 <see cref="PursuitState"/> 供弃牌采样、副露 / 报听约束使用。
/// 玩家不需要手动指定目标。
/// </para>
///
/// <para>
/// 进入条件（全部满足）：牌山 ≥ <see cref="EntryMinWall"/>（早期/中期）、非关键局
/// （<see cref="IsCritical"/> 为 false）、未和牌（听牌仍可追）、追役权重 ≥ 0.15、有候选目标的进度 ≥ 进入阈值。
/// 退出条件：牌山 &lt; <see cref="ExitWall"/>、目标不再可行（例如门清目标出现了明副露）、
/// 或进度比历史最好值下降 ≥ <see cref="ExitProgressDrop"/>。
/// </para>
///
/// <para>
/// 只读 <see cref="AiDecisionContext"/>（该玩家可见的信息），不修改对局状态；
/// 除 <see cref="Observe"/> 外都是纯函数。
/// </para>
/// </summary>
public sealed class AiPursuit
{
    /// <summary>进入追役要求的最少牌山张数（早期 / 中期）。</summary>
    public const int EntryMinWall = 35;

    /// <summary>牌山低于此值时自动退出追役。</summary>
    public const int ExitWall = 20;

    /// <summary>进入追役的最低目标进度（实际阈值随追役权重浮动）。</summary>
    public const double EntryMinProgress = 0.50;

    /// <summary>进度比进入以来最好值下降这么多就判定「牌局恶化」并退出。</summary>
    public const double ExitProgressDrop = 0.15;

    /// <summary>关键局判定：牌山低于此值视为终盘，不追稀有役。</summary>
    public const int CriticalWall = 25;

    /// <summary>关键局判定：与第一名分差达到此值视为大比分落后/领先。</summary>
    public const int DominanceGap = 20000;

    /// <summary>追役权重低于此值视为玩家不想追役，直接不进入。</summary>
    public const double MinPursuitWeight = 0.15;

    /// <summary>当前追役状态（未调用过 <see cref="Observe"/> 时为 <see cref="PursuitState.Inactive"/>）。</summary>
    public PursuitState Current { get; private set; } = PursuitState.Inactive;

    /// <summary>本实例累计进入追役次数（对局指标；<see cref="Reset"/> 不清零）。</summary>
    public int Entries { get; private set; }

    /// <summary>本实例累计退出追役次数（对局指标；<see cref="Reset"/> 不清零）。</summary>
    public int Exits { get; private set; }

    /// <summary>新的一手开始：清掉目标状态，保留累计指标。</summary>
    public void Reset() => Current = PursuitState.Inactive;

    /// <summary>新的一场开始：清掉累计指标。</summary>
    public void ResetStatistics()
    {
        Entries = 0;
        Exits = 0;
    }

    /// <summary>
    /// 根据当前手牌自动决定「是否追役 / 追什么 / 进度多少」。
    /// 同一手内反复调用会保留已选目标，直到恶化 / 不可行 / 牌山不够才退出。
    /// </summary>
    public PursuitState Observe(AiDecisionContext ctx, AiStyle style)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        style = (style ?? AiStyle.Default).Clamped();

        if (Current.Active)
        {
            if (!CanChase(ctx, style))
            {
                return ExitPursuit();
            }

            var snapshot = BuildSnapshot(ctx);
            var same = AssessTarget(Current.Target, snapshot, ctx);
            if (!same.Viable || same.Progress < Current.BestProgress - ExitProgressDrop)
            {
                return ExitPursuit();
            }

            Current = new PursuitState(
                Current.Target,
                true,
                same.Progress,
                Math.Max(Current.BestProgress, same.Progress),
                Temperature(style, same.Progress),
                same.Suit);
            return Current;
        }

        if (!CanChase(ctx, style))
        {
            return PursuitState.Inactive;
        }

        var best = ChooseBest(ctx, style);
        if (best is not { } candidate)
        {
            return PursuitState.Inactive;
        }

        Current = new PursuitState(
            candidate.Target,
            true,
            candidate.Progress,
            candidate.Progress,
            Temperature(style, candidate.Progress),
            candidate.Suit);
        Entries++;
        return Current;
    }

    // ─────────────────────────────────────────────────────────────
    // 静态规则（副露 / 报听 / 危险）
    // ─────────────────────────────────────────────────────────────

    /// <summary>该目标是否必须门清。</summary>
    public static bool RequiresClosed(PursuitTarget target) =>
        target is PursuitTarget.SevenPairs
            or PursuitTarget.BigSevenStars
            or PursuitTarget.NineGates
            or PursuitTarget.FourAnkan;

    /// <summary>
    /// 追役状态下是否允许副露。
    /// 字一色永远允许（唯一的例外）；其余门清目标禁止；混一色 / 清一色按副露与追役权重折算倾向，
    /// 目标花色以外的副露直接拒绝。
    /// </summary>
    public static bool AllowsMeld(
        PursuitState state,
        AiStyle style,
        IReadOnlyList<Tile>? meldTiles = null)
    {
        if (!state.Active)
        {
            return true;
        }

        if (state.Target == PursuitTarget.AllHonors)
        {
            return true;
        }

        if (state.RequiresClosed)
        {
            return false;
        }

        if (state.Suit is >= 0 and <= 2 && meldTiles is { Count: > 0 })
        {
            foreach (var tile in meldTiles)
            {
                if (!tile.IsHonor && (int)tile.Suit != state.Suit)
                {
                    return false;
                }
            }
        }

        double desire = Math.Clamp(style.Meld, AiStyle.Min, AiStyle.Max)
                        - 0.8
                        * Math.Max(0.0, Math.Clamp(style.Pursuit, AiStyle.Min, AiStyle.Max) - AiStyle.Neutral)
                        * Math.Clamp(state.Progress, 0.0, 1.0);
        return desire >= 0.5;
    }

    /// <summary>
    /// 追役状态下是否允许报听。报听会锁手，门清目标只有接近成型才值得锁；
    /// 字一色 / 混一色 / 清一色不禁止。
    /// </summary>
    public static bool AllowsCall(PursuitState state, AiStyle style)
    {
        if (!state.Active || !state.RequiresClosed)
        {
            return true;
        }

        double required = Math.Clamp(
            0.82 + 0.05 * (Math.Clamp(style.Pursuit, AiStyle.Min, AiStyle.Max) - AiStyle.Neutral),
            0.70,
            0.95);
        return state.Progress >= required;
    }

    /// <summary>
    /// 是否「关键局」（只求稳、不追稀有役）：终盘（牌山 &lt; <see cref="CriticalWall"/>），
    /// 或大比分落后 / 中后盘大比分领先。
    /// </summary>
    public static bool IsCritical(AiDecisionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (ctx.WallCount < CriticalWall)
        {
            return true;
        }

        if (ctx.Scores is not { Length: >= 4 })
        {
            return false;
        }

        int self = ctx.PlayerIndex is >= 0 and <= 3 ? ctx.PlayerIndex : (int)ctx.SeatWind;
        if (self is < 0 or > 3)
        {
            return false;
        }

        int leader = ctx.Scores[0];
        int last = ctx.Scores[0];
        for (int i = 1; i < 4; i++)
        {
            if (ctx.Scores[i] > leader)
            {
                leader = ctx.Scores[i];
            }

            if (ctx.Scores[i] < last)
            {
                last = ctx.Scores[i];
            }
        }

        // 大幅落后：先求稳；中后盘大幅领先：不冒险追稀有役。
        if (leader - ctx.Scores[self] >= DominanceGap)
        {
            return true;
        }

        return ctx.WallCount <= 40 && ctx.Scores[self] - last >= DominanceGap;
    }

    /// <summary>
    /// 追役状态下这张牌该不该保留（0..1，越大越不该打）。供策略网采样加 bonus 用。
    /// </summary>
    public static double KeepWeight(PursuitState state, AiDecisionContext ctx, Tile tile)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!state.Active)
        {
            return 0.0;
        }

        int count = 0;
        foreach (var handTile in ctx.Hand)
        {
            if (handTile == tile)
            {
                count++;
            }
        }

        return state.Target switch
        {
            PursuitTarget.SevenPairs => count >= 2 ? 1.0 : 0.15,
            PursuitTarget.AllHonors => tile.IsHonor ? 1.0 : 0.0,
            PursuitTarget.BigSevenStars => tile.IsHonor && tile.Num is >= 1 and <= 7 ? 1.0 : 0.0,
            PursuitTarget.NineGates => tile.Suit != Suit.Honor && (int)tile.Suit == state.Suit
                ? (tile.Num is 1 or 9 ? 1.15 : 1.0)
                : 0.0,
            PursuitTarget.Flush => tile.IsHonor
                ? 0.35
                : (int)tile.Suit == state.Suit ? 0.9 : 0.0,
            PursuitTarget.FourAnkan => count >= 3 ? 1.0 : count == 2 ? 0.5 : 0.0,
            _ => 0.0,
        };
    }

    /// <summary>
    /// 给「打掉这张」加的分数（0 起，越大越想打）：打出对目标没用的牌加分。
    /// 单位与策略网归一化后的 logit 同量级（约 0..2）。
    /// </summary>
    public static double DiscardBonus(PursuitState state, AiDecisionContext ctx, Tile tile, AiStyle style)
    {
        if (!state.Active)
        {
            return 0.0;
        }

        double keep = Math.Clamp(KeepWeight(state, ctx, tile), 0.0, 1.0);
        double progressScale = 0.7 + 0.8 * Math.Clamp(state.Progress, 0.0, 1.0);
        double attack = Math.Clamp(style.Attack, AiStyle.Min, AiStyle.Max);
        return (1.0 - keep) * progressScale * attack;
    }

    // ─────────────────────────────────────────────────────────────
    // 内部：进入 / 退出 / 目标评估
    // ─────────────────────────────────────────────────────────────

    private static bool CanChase(AiDecisionContext ctx, AiStyle style) =>
        style.Pursuit >= MinPursuitWeight
        && ctx.Hand.Count > 0
        && ctx.WallCount >= ExitWall
        && !IsCritical(ctx);

    private static bool CanEnter(AiDecisionContext ctx, AiStyle style) =>
        style.Pursuit >= MinPursuitWeight
        && ctx.Hand.Count >= 13
        && ctx.WallCount >= EntryMinWall
        && !IsCritical(ctx)
        && Shanten.Calculate(ctx.Hand, ctx.Melds).Shanten >= 0;   // 已和牌（-1）不再追役；听牌可以追

    private PursuitState ExitPursuit()
    {
        Current = PursuitState.Inactive;
        Exits++;
        return Current;
    }

    private static double EntryThreshold(AiStyle style) =>
        Math.Clamp(
            EntryMinProgress + (AiStyle.Neutral - Math.Clamp(style.Pursuit, AiStyle.Min, AiStyle.Max)) * 0.10,
            0.35,
            0.70);

    private static double Temperature(AiStyle style, double progress)
    {
        // 默认中性风格 ≈ 0.6–0.85：有采样、但不会明显乱打；追役权重越高越贪心。
        double commitment = Math.Clamp(style.Pursuit, 0.20, AiStyle.Max);
        double value = 0.60 / commitment + 0.25 * (1.0 - Math.Clamp(progress, 0.0, 1.0));
        return Math.Clamp(value, 0.25, 1.50);
    }

    private static TargetAssessment? ChooseBest(AiDecisionContext ctx, AiStyle style)
    {
        if (!CanEnter(ctx, style))
        {
            return null;
        }

        var snapshot = BuildSnapshot(ctx);
        double threshold = EntryThreshold(style);
        TargetAssessment? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (var target in AllTargets)
        {
            var assessment = AssessTarget(target, snapshot, ctx);
            if (!assessment.Viable || assessment.Progress < threshold)
            {
                continue;
            }

            // 进度为主，打点权重决定同进度时偏好高价值目标。
            double score = 0.6 * assessment.Progress
                           + 0.1 * Math.Clamp(style.Value, AiStyle.Min, AiStyle.Max) * assessment.Value;

            // 大七星是字一色的上位形态（七种字牌各一对）：结构可行时优先于字一色。
            if (target == PursuitTarget.BigSevenStars)
            {
                score += 0.25;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = assessment;
            }
        }

        return best;
    }

    private static readonly PursuitTarget[] AllTargets =
    {
        PursuitTarget.SevenPairs,
        PursuitTarget.Flush,
        PursuitTarget.AllHonors,
        PursuitTarget.BigSevenStars,
        PursuitTarget.NineGates,
        PursuitTarget.FourAnkan,
    };

    private static TargetAssessment AssessTarget(PursuitTarget target, in HandSnapshot snap, AiDecisionContext ctx)
    {
        switch (target)
        {
            case PursuitTarget.SevenPairs:
            {
                // 七对口径：sum(count/2) == 7，四张算两对。
                double progress = Math.Min(1.0, snap.PairUnits / 7.0);
                bool viable = snap.OpenMeldCount == 0 && snap.PairKinds >= 3;
                return new TargetAssessment(target, progress, 0.45, viable, -1);
            }

            case PursuitTarget.Flush:
            {
                if (snap.TotalTiles == 0)
                {
                    return new TargetAssessment(target, 0, 0, false, -1);
                }

                int dominant = 0;
                for (int suit = 1; suit < 3; suit++)
                {
                    if (snap.SuitAll[suit] > snap.SuitAll[dominant])
                    {
                        dominant = suit;
                    }
                }

                int combined = snap.SuitAll[dominant] + snap.HonorAll;
                double progress = combined / (double)snap.TotalTiles;
                double purity = combined == 0 ? 0.0 : snap.SuitAll[dominant] / (double)combined;
                bool viable = progress >= 0.55;
                return new TargetAssessment(target, progress, 0.55 + 0.25 * purity, viable, dominant);
            }

            case PursuitTarget.AllHonors:
            {
                if (snap.TotalTiles == 0)
                {
                    return new TargetAssessment(target, 0, 0, false, -1);
                }

                double progress = snap.HonorAll / (double)snap.TotalTiles;
                bool viable = snap.HonorAll >= 8 && progress >= 0.55;
                return new TargetAssessment(target, progress, 1.2, viable, -1);
            }

            case PursuitTarget.BigSevenStars:
            {
                // 大七星：七种字牌各一对，不能有四张（IsSevenHonors 口径）。
                double progress = snap.SevenHonorPairs / 7.0;
                bool viable = snap.TotalMeldCount == 0 && snap.SevenHonorPairs >= 3;
                return new TargetAssessment(target, progress, 1.5, viable, -1);
            }

            case PursuitTarget.NineGates:
            {
                if (snap.TotalMeldCount != 0 || snap.HandTiles == 0)
                {
                    return new TargetAssessment(target, 0, 0, false, -1);
                }

                int dominant = 0;
                for (int suit = 1; suit < 3; suit++)
                {
                    if (snap.SuitHand[suit] > snap.SuitHand[dominant])
                    {
                        dominant = suit;
                    }
                }

                int dominantCount = snap.SuitHand[dominant];
                bool hasOne = snap.Counts[dominant * 9] > 0;
                bool hasNine = snap.Counts[dominant * 9 + 8] > 0;
                double purity = dominantCount / (double)snap.HandTiles;
                double progress = 0.65 * purity + 0.35 * (hasOne && hasNine ? 1.0 : hasOne || hasNine ? 0.55 : 0.15);
                bool viable = dominantCount >= 9 && purity >= 0.65 && (hasOne || hasNine);
                return new TargetAssessment(target, Math.Min(1.0, progress), 1.5, viable, dominant);
            }

            case PursuitTarget.FourAnkan:
            {
                int ankan = ctx.AnkanCount;
                double progress = Math.Min(1.0, (ankan + 0.75 * snap.TripletKinds) / 4.0);
                bool viable = snap.OpenMeldCount == 0 && ankan >= 1 && progress >= 0.4;
                return new TargetAssessment(target, progress, 1.3, viable, -1);
            }

            default:
                return new TargetAssessment(PursuitTarget.None, 0, 0, false, -1);
        }
    }

    private static HandSnapshot BuildSnapshot(AiDecisionContext ctx)
    {
        var counts = new int[36];
        int handTiles = 0;
        foreach (var tile in ctx.Hand)
        {
            counts[tile.Index]++;
            handTiles++;
        }

        var suitHand = new int[3];
        int honorHand = 0;
        int pairUnits = 0;
        int pairKinds = 0;
        int tripletKinds = 0;
        int sevenHonorPairs = 0;
        for (int index = 0; index < 34; index++)
        {
            int count = counts[index];
            if (index < 27)
            {
                suitHand[index / 9] += count;
            }
            else
            {
                honorHand += count;
            }

            if (count >= 2)
            {
                pairKinds++;
                pairUnits += count / 2;
                if (index >= 27 && count == 2)
                {
                    sevenHonorPairs++;
                }
            }

            if (count >= 3)
            {
                tripletKinds++;
            }
        }

        var suitAll = new int[3];
        Array.Copy(suitHand, suitAll, 3);
        int honorAll = honorHand;
        int meldTiles = 0;
        int openMeldCount = 0;
        foreach (var meld in ctx.Melds)
        {
            meldTiles += meld.Tiles.Count;
            if (meld.Type != MeldType.Ankan)
            {
                openMeldCount++;
            }

            foreach (var tile in meld.Tiles)
            {
                if (tile.IsHonor)
                {
                    honorAll++;
                }
                else if (tile.Index < 27)
                {
                    suitAll[tile.Index / 9]++;
                }
            }
        }

        return new HandSnapshot(
            counts,
            handTiles,
            meldTiles,
            suitHand,
            suitAll,
            honorHand,
            honorAll,
            pairUnits,
            pairKinds,
            tripletKinds,
            sevenHonorPairs,
            openMeldCount,
            ctx.Melds.Count);
    }

    private readonly record struct TargetAssessment(
        PursuitTarget Target,
        double Progress,
        double Value,
        bool Viable,
        int Suit);

    private readonly struct HandSnapshot
    {
        public HandSnapshot(
            int[] counts,
            int handTiles,
            int meldTiles,
            int[] suitHand,
            int[] suitAll,
            int honorHand,
            int honorAll,
            int pairUnits,
            int pairKinds,
            int tripletKinds,
            int sevenHonorPairs,
            int openMeldCount,
            int totalMeldCount)
        {
            Counts = counts;
            HandTiles = handTiles;
            MeldTiles = meldTiles;
            SuitHand = suitHand;
            SuitAll = suitAll;
            HonorHand = honorHand;
            HonorAll = honorAll;
            PairUnits = pairUnits;
            PairKinds = pairKinds;
            TripletKinds = tripletKinds;
            SevenHonorPairs = sevenHonorPairs;
            OpenMeldCount = openMeldCount;
            TotalMeldCount = totalMeldCount;
        }

        public int[] Counts { get; }

        public int HandTiles { get; }

        public int MeldTiles { get; }

        public int TotalTiles => HandTiles + MeldTiles;

        public int[] SuitHand { get; }

        public int[] SuitAll { get; }

        public int HonorHand { get; }

        public int HonorAll { get; }

        public int PairUnits { get; }

        public int PairKinds { get; }

        public int TripletKinds { get; }

        public int SevenHonorPairs { get; }

        public int OpenMeldCount { get; }

        public int TotalMeldCount { get; }
    }
}
