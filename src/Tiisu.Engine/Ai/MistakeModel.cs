using Tiisu.Engine.Random;

namespace Tiisu.Engine.Ai;

/// <summary>
/// 失误 / 紧张度模型（A 期）：基线失误率按档位给定，紧张度高时追加 1–3 个百分点。
///
/// <para>
/// 紧张度由三部分线性合成：接近流局（牌山剩余少）、分数落后、对手副露多。
/// 失误表现由上层统一处理为「换成第二名候选」（在 Hard 档放弃安全分时即表现为不防守）。
/// </para>
/// <para>
/// 设计文档明确「修罗档不注入失误」，因此 Shura 无论紧张度如何都返回 0。
/// </para>
/// </summary>
public static class MistakeModel
{
    /// <summary>各档基线失误率：Simple 20% / Normal 8% / Hard 2% / Shura 0%。</summary>
    public static double BaselineFailureRate(AiLevel level) => level switch
    {
        AiLevel.Simple => 0.20,
        AiLevel.Normal => 0.08,
        AiLevel.Hard => 0.02,
        _ => 0.0,
    };

    /// <summary>紧张度 0..1：牌山越少、落后越多、对手副露越多越高。</summary>
    public static double Tension(AiDecisionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        double wall = Clamp01(1.0 - ctx.WallCount / 70.0);
        double behind = ScoreTension(ctx);
        double threat = Clamp01(ctx.OpponentMelds.Count / 6.0);

        return Clamp01(wall * 0.4 + behind * 0.3 + threat * 0.3);
    }

    /// <summary>最终失误率：基线 +（紧张度超过阈值时）1–3 个百分点。</summary>
    public static double FailureRate(AiDecisionContext ctx, AiLevel level, AiTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        tuning ??= AiTuning.Default;

        double baseline = BaselineFailureRate(level);
        if (level == AiLevel.Shura)
        {
            return 0.0;
        }

        double tension = Tension(ctx);
        if (tension <= tuning.MistakeTensionThreshold)
        {
            return baseline;
        }

        double span = (tension - tuning.MistakeTensionThreshold)
            / Math.Max(1e-9, 1.0 - tuning.MistakeTensionThreshold);
        double bonus = tuning.MistakeTensionMinBonus
            + (tuning.MistakeTensionMaxBonus - tuning.MistakeTensionMinBonus) * Clamp01(span);

        return Clamp01(baseline + bonus);
    }

    /// <summary>按注入随机源判定本次是否失误（同一个随机序列可复现）。</summary>
    public static bool ShouldMistake(
        AiDecisionContext ctx,
        AiLevel level,
        IRandomSource rng,
        AiTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(rng);

        double rate = FailureRate(ctx, level, tuning);
        if (rate <= 0.0)
        {
            return false;
        }

        if (rate >= 1.0)
        {
            return true;
        }

        return rng.NextDouble() < rate;
    }

    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 分数落后度。优先按 <see cref="AiDecisionContext.PlayerIndex"/> 取自家分数；
    /// 未提供时兼容按座次（东→南→西→北）取 <c>Scores[(int)SeatWind]</c>。
    /// </summary>
    private static double ScoreTension(AiDecisionContext ctx)
    {
        if (ctx.Scores is not { Length: >= 4 })
        {
            return 0.0;
        }

        int ownIndex = ctx.PlayerIndex is >= 0 and <= 3 ? ctx.PlayerIndex : (int)ctx.SeatWind;
        int own = ctx.Scores[ownIndex];
        int leader = ctx.Scores[0];
        for (int i = 1; i < 4; i++)
        {
            if (ctx.Scores[i] > leader)
            {
                leader = ctx.Scores[i];
            }
        }

        if (leader <= own)
        {
            return 0.0;
        }

        return Clamp01((leader - own) / 20000.0);
    }

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
}
