namespace Tiisu.Engine.Ai;

/// <summary>
/// 分档 AI 的可调参数集中处（A 期）。默认值面向「陪玩」：Normal 以进张为主，
/// Hard 在进张之外加入打点与粗略安全，Simple 保留旧启发式加噪声。
/// </summary>
public sealed class AiTuning
{
    public static AiTuning Default { get; } = new();

    /// <summary>Simple 档叠加的均匀噪声幅度（分数单位；旧启发式分值大致在 -40..80）。</summary>
    public double SimpleNoise { get; init; } = 2.0;

    /// <summary>Normal 档进张权重；同进张时用旧启发式做平手。</summary>
    public double NormalUkeireWeight { get; init; } = 1000.0;

    /// <summary>Normal 档旧启发式平手权重（只影响进张相同的候选）。</summary>
    public double NormalHeuristicTiebreak { get; init; } = 1.0;

    /// <summary>Hard 档进张权重。</summary>
    public double HardUkeireWeight { get; init; } = 10.0;

    /// <summary>Hard 档听牌后期望赋的权重。</summary>
    public double HardValueWeight { get; init; } = 1.0;

    /// <summary>Hard 档现物安全加分（牌在任意一家牌河出现过）。略高于单张进张权重，避免为 1 张进张放弃现物。</summary>
    public double HardGenbutsuWeight { get; init; } = 12.0;

    /// <summary>
    /// Hard 档放铳危险度权重（B 期）。危险度是 0..1，进张权重是每张 10 分，
    /// 因此 0.33 左右的危险差 ≈ 1 张进张；默认 30 表示「为安全可以少约 3 张进张」。
    /// </summary>
    public double HardDangerWeight { get; init; } = 30.0;

    /// <summary>Hard 档旧启发式平手权重。</summary>
    public double HardHeuristicTiebreak { get; init; } = 0.01;

    /// <summary>Normal 档残局防守权重（只在残局且自家向听 ≥ 2 时生效）。</summary>
    public double NormalDangerWeight { get; init; } = 5.0;

    /// <summary>Normal 档开始考虑防守的牌山剩余张数阈值（含）。</summary>
    public int NormalDangerWallThreshold { get; init; } = 30;

    /// <summary>Normal 档启用残局防守时的最小自家向听（≥ 此值才防守，越接近听牌越不弃安全）。</summary>
    public int NormalDangerShantenThreshold { get; init; } = 2;

    /// <summary>自适应思考：top1 与 top2 相对分差低于此值时标记 NeedDeepThink。</summary>
    public double DeepThinkScoreGap { get; init; } = 0.05;

    /// <summary>自适应思考：最佳候选中打后向听 ≤ 此值时标记 NeedDeepThink（0=听牌，1=进听）。</summary>
    public int DeepThinkShantenThreshold { get; init; } = 1;

    /// <summary>紧张度超过此阈值才在基线失误率上追加 1–3 个百分点。</summary>
    public double MistakeTensionThreshold { get; init; } = 0.35;

    /// <summary>紧张度拉满时的最小追加失误率（1 个百分点）。</summary>
    public double MistakeTensionMinBonus { get; init; } = 0.01;

    /// <summary>紧张度拉满时的最大追加失误率（3 个百分点）。</summary>
    public double MistakeTensionMaxBonus { get; init; } = 0.03;
}
