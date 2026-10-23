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

    // ─── 报听（叫胡）──────────────────────────────────────────────

    /// <summary>Hard 档报听的最低期望赋（荣和口径，按待牌剩余张数加权）。</summary>
    public double HardCallMinExpectedFu { get; init; } = 7.0;

    /// <summary>Shura 档报听的最低期望赋；比 Hard 更严。</summary>
    public double ShuraCallMinExpectedFu { get; init; } = 10.0;

    /// <summary>Hard 档报听允许的最大放铳危险度（0..1，只算要打出的那张）。</summary>
    public double HardCallMaxDanger { get; init; } = 0.60;

    /// <summary>Shura 档报听允许的最大放铳危险度；比 Hard 更严。</summary>
    public double ShuraCallMaxDanger { get; init; } = 0.35;

    /// <summary>自适应思考：top1 与 top2 相对分差低于此值时标记 NeedDeepThink。</summary>
    public double DeepThinkScoreGap { get; init; } = 0.05;

    /// <summary>自适应思考：最佳候选中打后向听 ≤ 此值时标记 NeedDeepThink（0=听牌，1=进听）。</summary>
    public int DeepThinkShantenThreshold { get; init; } = 1;

    // ─── C 期：修罗档 MCTS（实验性，默认关闭）───────────────────────

    /// <summary>MCTS 参与评分的候选数上限（按 Hard 评分取前 K）。</summary>
    public int MctsCandidateLimit { get; init; } = 8;

    /// <summary>普通局面的模拟次数。</summary>
    public int MctsNormalSimulations { get; init; } = 100;

    /// <summary>关键局面（NeedDeepThink）的模拟次数。</summary>
    public int MctsDeepSimulations { get; init; } = 300;

    /// <summary>单次 rollout 最多摸几张牌。</summary>
    public int MctsMaxRolloutDraws { get; init; } = 12;

    /// <summary>兜底时间预算（毫秒）；超时提前停，不再开新模拟。</summary>
    public int MctsTimeBudgetMs { get; init; } = 200;

    /// <summary>和牌率 × 期望赋 的权重。</summary>
    public double MctsWinWeight { get; init; } = 1.0;

    /// <summary>期望赋缺失（未听牌）时用于折算和牌率的基础值。</summary>
    public double MctsBaseWinValue { get; init; } = 20.0;

    /// <summary>听牌率权重。</summary>
    public double MctsTenpaiWeight { get; init; } = 40.0;

    /// <summary>放铳危险度权重（与 Hard 同量级：0.33 危险 ≈ 1 张进张）。</summary>
    public double MctsDangerWeight { get; init; } = 30.0;

    /// <summary>rollout 里「不按最优打」的概率（少量随机）。</summary>
    public double MctsRolloutBlunder { get; init; } = 0.10;

    /// <summary>紧张度超过此阈值才在基线失误率上追加 1–3 个百分点。</summary>
    public double MistakeTensionThreshold { get; init; } = 0.35;

    /// <summary>紧张度拉满时的最小追加失误率（1 个百分点）。</summary>
    public double MistakeTensionMinBonus { get; init; } = 0.01;

    /// <summary>紧张度拉满时的最大追加失误率（3 个百分点）。</summary>
    public double MistakeTensionMaxBonus { get; init; } = 0.03;
}
