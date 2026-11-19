using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tiisu.Engine.Random;
using Tiisu.Engine.SelfPlay;

namespace Tiisu.Engine.Ai;

/// <summary>策略网对一张手牌的原始打分（手牌下标 + 牌 + 34 类 logit）。</summary>
public readonly record struct NeuralDiscardScore(int Index, Tile Tile, float Logit);

/// <summary>
/// 小策略网推理（P2-1）：支持 v2（181 维）与 v4（339 维，自博弈特征）权重。
/// 权重由 <c>ml/train_policy.py</c> 导出；文件缺失时返回 -1，调用方回退 <see cref="TieredAi"/>。
///
/// <para>
/// 默认路径（<see cref="ChooseDiscard(AiDecisionContext)"/>）是纯 argmax，行为与旧版完全一致；
/// 追役原型走 <see cref="ChooseDiscard(AiDecisionContext, AiStyle, PursuitState, IRandomSource, string?)"/>：
/// 普通状态仍 argmax，只有 <see cref="PursuitState.Active"/> 时才按温度采样并叠加目标进度 bonus。
/// </para>
/// </summary>
public static class NeuralPolicyAi
{
    private const int V2FeatureCount = 181;
    private const int PlayerSlots = 4;

    /// <summary>追役采样里放铳危险度的权重（危险度 0..1 → 分数单位）。</summary>
    private const double PursuitDangerWeight = 1.2;

    private static readonly object WeightLock = new();
    private static readonly object DiagnosticsLock = new();

    /// <summary>按权重文件绝对路径缓存（A/B 同一进程里不同座位用不同文件时复用）。</summary>
    private static readonly Dictionary<string, Weights> WeightCache = new(StringComparer.OrdinalIgnoreCase);

    private static Weights? _defaultWeights;
    private static bool _defaultLoadTried;
    private static string? _defaultLoadedFrom;

    /// <summary>默认权重来源（生产 = v4；A/B 按座位传路径不会改这个值）。</summary>
    public static string? LoadedFrom
    {
        get
        {
            lock (WeightLock)
            {
                return _defaultLoadedFrom;
            }
        }
    }

    /// <summary>评测开关：为 true 时按座位累计每次策略网推理的动作分布熵（正常游戏默认 false）。</summary>
    public static bool CollectDiagnostics { get; set; }

    private static readonly double[] EntropySumByPlayer = new double[PlayerSlots];
    private static readonly long[] DecisionsByPlayer = new long[PlayerSlots];

    /// <summary>某座位累计参与熵统计的策略网决策次数。</summary>
    public static long DiagnosticsDecisionsForPlayer(int player)
    {
        if (player is < 0 or >= PlayerSlots)
        {
            return 0;
        }

        lock (DiagnosticsLock)
        {
            return DecisionsByPlayer[player];
        }
    }

    /// <summary>某座位的平均动作分布熵（自然对数；34 类 softmax）；没有决策时为 0。</summary>
    public static double DiagnosticsAverageEntropyForPlayer(int player)
    {
        if (player is < 0 or >= PlayerSlots)
        {
            return 0.0;
        }

        lock (DiagnosticsLock)
        {
            return DecisionsByPlayer[player] == 0
                ? 0.0
                : EntropySumByPlayer[player] / DecisionsByPlayer[player];
        }
    }

    /// <summary>清空全部座位的熵统计。</summary>
    public static void ResetDiagnostics()
    {
        lock (DiagnosticsLock)
        {
            Array.Clear(EntropySumByPlayer);
            Array.Clear(DecisionsByPlayer);
        }
    }

    public static bool Available => DefaultWeights() is not null;

    /// <summary>返回弃牌在 <see cref="AiDecisionContext.Hand"/> 里的下标；无权重返回 -1（生产默认路径）。</summary>
    public static int ChooseDiscard(AiDecisionContext ctx) => ChooseDiscard(ctx, weightsPath: null);

    /// <summary>
    /// 返回弃牌下标。权重按 <paramref name="weightsPath"/> 选：
    /// <list type="bullet">
    /// <item>null = 生产默认发现顺序（v4 优先），行为与旧版完全一致；</item>
    /// <item>非 null = 显式权重文件（按 JSON 的 <c>features</c> 字段自动选 v2 181 维 / v4 339 维），
    /// 按文件绝对路径缓存，同一个进程/对局里可以让焦点座位用 v2、其余三家仍用 v4（没有全局 override）。</item>
    /// </list>
    /// 默认权重缺失返回 -1（调用方回退 <see cref="TieredAi"/>）；显式路径缺失 / 非法抛异常（A/B 配置错误必须炸）。
    /// </summary>
    public static int ChooseDiscard(AiDecisionContext ctx, string? weightsPath)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var weights = ResolveWeights(weightsPath);
        return weights is null ? -1 : Argmax(ScoreDiscardsWith(ctx, weights));
    }

    /// <summary>
    /// 策略网对每张手牌的原始 logit（按手牌下标，与 <see cref="AiDecisionContext.Hand"/> 一一对应）；
    /// <paramref name="weightsPath"/> 语义同 <see cref="ChooseDiscard(AiDecisionContext, string?)"/>。
    /// </summary>
    public static IReadOnlyList<NeuralDiscardScore> ScoreDiscards(AiDecisionContext ctx, string? weightsPath = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var weights = ResolveWeights(weightsPath);
        return weights is null ? Array.Empty<NeuralDiscardScore>() : ScoreDiscardsWith(ctx, weights);
    }

    /// <summary>
    /// 7Neuu + 自动追役原型的弃牌：
    /// <list type="bullet">
    /// <item>普通状态（<see cref="PursuitState.Active"/> = false）：与 <see cref="ChooseDiscard(AiDecisionContext)"/>
    /// 完全相同的 argmax（玩家五项风格权重不改变普通弃牌）。</item>
    /// <item>追役状态：logit 做 z-score 归一化后，叠加 <see cref="AiPursuit.DiscardBonus"/> 与防守危险惩罚，
    /// 用 <see cref="PursuitState.Temperature"/> 做 softmax 温度采样。</item>
    /// </list>
    /// <paramref name="weightsPath"/> 语义同 <see cref="ChooseDiscard(AiDecisionContext, string?)"/>：
    /// null = 默认 v4，非 null = 该座位显式权重（A/B 焦点座位 v2）。
    /// 无权重返回 -1（调用方回退 <see cref="TieredAi"/>）。
    /// </summary>
    public static int ChooseDiscard(
        AiDecisionContext ctx,
        AiStyle style,
        PursuitState pursuit,
        IRandomSource rng,
        string? weightsPath = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(rng);
        style = (style ?? AiStyle.Default).Clamped();

        var picks = ScoreDiscards(ctx, weightsPath);
        if (picks.Count == 0)
        {
            return -1;
        }

        // 普通强度模式：argmax（与旧版行为逐位一致）。
        if (!pursuit.Active)
        {
            return Argmax(picks);
        }

        int n = picks.Count;
        var values = new double[n];
        double mean = 0.0;
        for (int i = 0; i < n; i++)
        {
            values[i] = picks[i].Logit;
            mean += values[i];
        }

        mean /= n;
        double variance = 0.0;
        for (int i = 0; i < n; i++)
        {
            double delta = values[i] - mean;
            variance += delta * delta;
        }

        double std = Math.Sqrt(variance / n);
        double inverseStd = std > 1e-6 ? 1.0 / std : 0.0;

        ThreatInput? threat = null;
        double defense = Math.Clamp(style.Defense, AiStyle.Min, AiStyle.Max);
        if (defense > 1e-6)
        {
            threat = TieredAi.BuildThreatInput(ctx, TieredAi.Rivers(ctx));
        }

        for (int i = 0; i < n; i++)
        {
            values[i] = (values[i] - mean) * inverseStd;
            values[i] += AiPursuit.DiscardBonus(pursuit, ctx, picks[i].Tile, style);
            if (threat is not null)
            {
                values[i] -= ThreatModel.Danger(picks[i].Tile, threat) * PursuitDangerWeight * defense;
            }

            if (!double.IsFinite(values[i]))
            {
                // finite 检查：任何 NaN / ∞ 都退回 argmax，不让坏值污染采样。
                return Argmax(picks);
            }
        }

        int sampled = SampleScores(values, pursuit.Temperature, rng);
        return sampled < 0 ? Argmax(picks) : picks[sampled].Index;
    }

    /// <summary>
    /// 从已加好 bonus 的分数里按 softmax(1/T) 采一个下标。T ≤ 0 / 非有限时退化为 argmax。
    /// 供 <see cref="ChooseDiscard(AiDecisionContext, AiStyle, PursuitState, IRandomSource, string?)"/> 与单测使用。
    /// </summary>
    internal static int SampleScores(double[] scores, double temperature, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(rng);
        if (scores.Length == 0)
        {
            return -1;
        }

        int best = 0;
        for (int i = 0; i < scores.Length; i++)
        {
            if (!double.IsFinite(scores[i]))
            {
                return BestIndex(scores);
            }

            if (scores[i] > scores[best])
            {
                best = i;
            }
        }

        double t = double.IsFinite(temperature) && temperature > 0.0
            ? Math.Clamp(temperature, 0.05, 5.0)
            : 0.0;
        if (t <= 0.0)
        {
            return best;
        }

        double max = scores[best];
        var weights = new double[scores.Length];
        double total = 0.0;
        for (int i = 0; i < scores.Length; i++)
        {
            double z = (scores[i] - max) / t;
            double weight = z < -40.0 ? 0.0 : Math.Exp(z);
            weights[i] = weight;
            total += weight;
        }

        if (!(total > 0.0) || !double.IsFinite(total))
        {
            return best;
        }

        double roll = rng.NextDouble() * total;
        double accumulated = 0.0;
        for (int i = 0; i < weights.Length; i++)
        {
            accumulated += weights[i];
            if (roll < accumulated)
            {
                return i;
            }
        }

        return best;
    }

    private static int BestIndex(double[] scores)
    {
        int best = 0;
        for (int i = 1; i < scores.Length; i++)
        {
            if (scores[i] > scores[best])
            {
                best = i;
            }
        }

        return best;
    }

    private static int Argmax(IReadOnlyList<NeuralDiscardScore> picks)
    {
        int best = -1;
        float bestScore = float.NegativeInfinity;
        foreach (var pick in picks)
        {
            if (pick.Logit > bestScore)
            {
                bestScore = pick.Logit;
                best = pick.Index;
            }
        }

        return best;
    }

    private static List<NeuralDiscardScore> ScoreDiscardsWith(AiDecisionContext ctx, Weights w)
    {
        float[] x = Normalize(BuildFeatures(ctx, w.Features), w);
        float[] h1 = Dense(w.L1, w.B1, x, w.Hidden);
        float[] h2 = Dense(w.L2, w.B2, h1, w.Hidden);
        float[] logits = Dense(w.L3, w.B3, h2, 34);
        AccumulateDiagnostics(logits, ctx.PlayerIndex);

        var picks = new List<NeuralDiscardScore>(ctx.Hand.Count);
        for (int i = 0; i < ctx.Hand.Count; i++)
        {
            int idx = TileIndex(ctx.Hand[i]);
            if (idx >= 0)
            {
                picks.Add(new NeuralDiscardScore(i, ctx.Hand[i], logits[idx]));
            }
        }

        return picks;
    }

    /// <summary>一次策略网输出的动作分布熵（34 类 softmax，自然对数）；供 A/B 诊断与单测。</summary>
    internal static double EntropyOf(IReadOnlyList<float> logits)
    {
        ArgumentNullException.ThrowIfNull(logits);
        if (logits.Count == 0)
        {
            return 0.0;
        }

        float max = logits[0];
        for (int i = 1; i < logits.Count; i++)
        {
            if (logits[i] > max)
            {
                max = logits[i];
            }
        }

        var exp = new double[logits.Count];
        double total = 0.0;
        for (int i = 0; i < logits.Count; i++)
        {
            double value = Math.Exp(logits[i] - max);
            exp[i] = value;
            total += value;
        }

        if (!(total > 0.0) || !double.IsFinite(total))
        {
            return 0.0;
        }

        double entropy = 0.0;
        for (int i = 0; i < exp.Length; i++)
        {
            double p = exp[i] / total;
            if (p > 0.0)
            {
                entropy -= p * Math.Log(p);
            }
        }

        return double.IsFinite(entropy) ? entropy : 0.0;
    }

    private static void AccumulateDiagnostics(float[] logits, int player)
    {
        if (!CollectDiagnostics || player is < 0 or >= PlayerSlots)
        {
            return;
        }

        double entropy = EntropyOf(logits);
        lock (DiagnosticsLock)
        {
            EntropySumByPlayer[player] += entropy;
            DecisionsByPlayer[player]++;
        }
    }

      private static float[] BuildFeatures(AiDecisionContext ctx, int featureCount) =>
          featureCount >= SelfPlayFeatures.Count ? BuildFeaturesV4(ctx) : BuildFeaturesV2(ctx);

      private static float[] BuildFeaturesV2(AiDecisionContext ctx)
    {
        var x = new float[V2FeatureCount];

        // 0..33 手牌
        foreach (var tile in ctx.Hand)
        {
            int idx = TileIndex(tile);
            if (idx >= 0)
            {
                x[idx] += 1;
            }
        }

        // 34..169 四家牌河（每人每张出现次数 / 4）
        for (int seat = 0; seat < 4; seat++)
        {
            IReadOnlyList<Tile>? river = seat < ctx.AllDiscards.Count ? ctx.AllDiscards[seat] : null;
            if (river is null)
            {
                continue;
            }

            foreach (var tile in river)
            {
                int idx = TileIndex(tile);
                if (idx >= 0)
                {
                    x[34 + seat * 34 + idx] += 0.25f;
                }
            }
        }

        // 170..173 四家副露数
        for (int seat = 0; seat < 4; seat++)
        {
            int count;
            if (ctx.AllMelds is { } all && seat < all.Count)
            {
                count = all[seat].Count;
            }
            else if (seat == (int)ctx.SeatWind)
            {
                count = ctx.Melds.Count;
            }
            else
            {
                count = ctx.OpponentMelds.Count;
            }

            x[170 + seat] = Math.Min(count, 4) / 4f;
        }

        // 174..180 场况
        x[174] = Math.Min(ctx.WallCount, 83) / 83f;
        x[175] = Math.Min(ctx.Discards.Count, 18) / 18f;
        x[176] = Math.Min(ctx.Honba, 8) / 8f;
        x[177] = ctx.SeatWind == SeatWind.East ? 1f : 0f;
        int s = (int)ctx.SeatWind;
        if (s is 1 or 2 or 3)
        {
            x[178 + (s - 1)] = 1f;
        }

        return x;
    }

      private static float[] BuildFeaturesV4(AiDecisionContext ctx)
      {
          var x = new float[SelfPlayFeatures.Count];

          foreach (var tile in ctx.Hand)
          {
              int idx = TileIndex(tile);
              if (idx >= 0) x[idx] += 1f;
          }

          for (int seat = 0; seat < 4; seat++)
          {
              if (seat >= ctx.AllDiscards.Count) continue;
              foreach (var tile in ctx.AllDiscards[seat])
              {
                  int idx = TileIndex(tile);
                  if (idx >= 0) x[34 + seat * 34 + idx] += 0.25f;
              }
          }

          for (int seat = 0; seat < 4; seat++)
          {
              IReadOnlyList<Meld> melds;
              if (ctx.AllMelds is { } all && seat < all.Count)
              {
                  melds = all[seat];
              }
              else if (seat == (int)ctx.SeatWind)
              {
                  melds = ctx.Melds;
              }
              else
              {
                  melds = ctx.OpponentMelds;
              }

              x[306 + seat] = Math.Min(melds.Count, 4) / 4f;
              foreach (var meld in melds)
              {
                  foreach (var tile in meld.Tiles)
                  {
                      int idx = TileIndex(tile);
                      if (idx >= 0) x[170 + seat * 34 + idx] += 0.25f;
                  }
              }
          }

          double avg = 0;
          for (int i = 0; i < 4; i++) avg += ctx.Scores[i];
          avg /= 4;
          for (int seat = 0; seat < 4; seat++)
          {
              x[310 + seat] = ctx.Scores[seat] / 10000f;
              x[314 + seat] = (float)((ctx.Scores[seat] - avg) / 10000.0);
          }

          x[318] = Math.Min(ctx.Honba, 8) / 8f;
          x[319 + (int)ctx.RoundWind] = 1f;
          x[323 + (int)ctx.SeatWind] = 1f;

          var pattern = HandPatternFeatures.Build(ctx.Hand);
          Array.Copy(pattern, 0, x, 327, pattern.Length);
          return x;
      }


    private static float[] Normalize(float[] x, Weights w)
    {
        if (w.FeatureMean.Length != x.Length || w.FeatureStd.Length != x.Length)
        {
            return x;
        }

        for (int i = 0; i < x.Length; i++)
        {
            float std = w.FeatureStd[i];
            if (std > 1e-6f)
            {
                x[i] = (x[i] - w.FeatureMean[i]) / std;
            }
        }

        return x;
    }


    private static int TileIndex(Tile tile)
    {
        int idx = tile.Index;
        return idx is >= 0 and < 34 ? idx : -1;
    }

    private static float[] Dense(float[] w, float[] b, float[] x, int outDim)
    {
        int inDim = x.Length;
        var y = new float[outDim];
        for (int o = 0; o < outDim; o++)
        {
            float sum = b[o];
            int row = o * inDim;
            for (int i = 0; i < inDim; i++)
            {
                sum += w[row + i] * x[i];
            }

            y[o] = sum > 0 ? sum : 0;
        }

        return y;
    }

    /// <summary>
    /// 解析本次决策用的权重：
    /// <list type="bullet">
    /// <item>路径为空 → 生产默认（v4 优先；缺失返回 null，调用方回退 TieredAi）；</item>
    /// <item>路径非空 → 该文件的缓存权重（v2 181 维 / v4 339 维按 features 自动选）；缺失 / 非法抛异常。</item>
    /// </list>
    /// 关键：路径只影响这一次决策，不做全局覆盖 —— 同一进程里不同座位可以各自用自己的权重文件。
    /// </summary>
    private static Weights? ResolveWeights(string? weightsPath)
    {
        if (string.IsNullOrWhiteSpace(weightsPath))
        {
            return DefaultWeights();
        }

        string full = Path.GetFullPath(weightsPath);
        lock (WeightLock)
        {
            if (WeightCache.TryGetValue(full, out var cached))
            {
                return cached;
            }
        }

        var loaded = TryLoadWeights(full)
            ?? throw new InvalidOperationException($"策略权重文件缺失或非法: {full}");
        lock (WeightLock)
        {
            WeightCache[full] = loaded;
        }

        return loaded;
    }

    /// <summary>生产默认权重（v4 优先，其次 v2 / 旧名）；A/B 的按座位路径不影响它。</summary>
    private static Weights? DefaultWeights()
    {
        lock (WeightLock)
        {
            if (_defaultLoadTried)
            {
                return _defaultWeights;
            }

            _defaultLoadTried = true;
            foreach (string path in CandidatePaths())
            {
                if (TryLoadWeights(path) is { } weights)
                {
                    _defaultWeights = weights;
                    _defaultLoadedFrom = path;
                    return weights;
                }
            }

            return null;
        }
    }

    /// <summary>从单个文件读权重；损坏 / 非权重文件返回 null（调用方决定回退还是报错）。</summary>
    private static Weights? TryLoadWeights(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is not JsonObject obj || obj["layers"] is not JsonArray layers || layers.Count < 3)
            {
                return null;
            }

            var weights = new Weights
            {
                Hidden = (int?)obj["hidden"] ?? 256,
                Features = (int?)obj["features"] ?? 181,
                FeatureMean = obj["feature_mean"] is JsonArray m ? ReadVector(m) : Array.Empty<float>(),
                FeatureStd = obj["feature_std"] is JsonArray st ? ReadVector(st) : Array.Empty<float>(),
                L1 = ReadMatrix(layers[0]!["w"]),
                B1 = ReadVector(layers[0]!["b"]),
                L2 = ReadMatrix(layers[1]!["w"]),
                B2 = ReadVector(layers[1]!["b"]),
                L3 = ReadMatrix(layers[2]!["w"]),
                B3 = ReadVector(layers[2]!["b"]),
            };

            // 维度校验：坏权重不进入推理（否则 Dense 会越界 / 静默错位）。
            if (weights.Hidden <= 0 || weights.Features <= 0
                || weights.L1.Length != weights.Hidden * weights.Features
                || weights.B1.Length != weights.Hidden
                || weights.L2.Length != weights.Hidden * weights.Hidden
                || weights.B2.Length != weights.Hidden
                || weights.L3.Length != 34 * weights.Hidden
                || weights.B3.Length != 34)
            {
                return null;
            }

            return weights;
        }
        catch
        {
            // 坏文件不抛：候选路径循环继续；显式 override 由调用方报错。
            return null;
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        string? env = Tiisu.Core.TiisuConfig.Current.PolicyWeights;
        if (!string.IsNullOrEmpty(env))
        {
            yield return env!;
        }

        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Assets", "ml", "policy_weights_v4.json");
        yield return Path.Combine(baseDir, "Assets", "ml", "policy_weights_v2.json");
        yield return Path.Combine(baseDir, "Assets", "ml", "policy_weights.json");
        yield return Path.Combine(baseDir, "policy_weights.json");

        // 开发期：从 bin 目录往上找仓库里的 ml/
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir is not null; i++)
        {
            yield return Path.Combine(dir.FullName, "ml", "policy_weights_v4.json");
            yield return Path.Combine(dir.FullName, "ml", "policy_weights_v2.json");
            yield return Path.Combine(dir.FullName, "ml", "policy_weights.json");
            dir = dir.Parent;
        }
    }

    private static float[] ReadVector(JsonNode? node) =>
        (node as JsonArray)?.Select(v => (float)(double?)v!).ToArray() ?? Array.Empty<float>();

    private static float[] ReadMatrix(JsonNode? node)
    {
        if (node is not JsonArray rows)
        {
            return Array.Empty<float>();
        }

        int cols = (rows[0] as JsonArray)?.Count ?? 0;
        var flat = new float[rows.Count * cols];
        int k = 0;
        foreach (var row in rows)
        {
            foreach (var v in (JsonArray)row!)
            {
                flat[k++] = (float)(double?)v!;
            }
        }

        return flat;
    }

    private sealed class Weights
    {
        public int Hidden { get; init; }
          public int Features { get; init; } = 181;
          public float[] FeatureMean { get; init; } = Array.Empty<float>();
          public float[] FeatureStd { get; init; } = Array.Empty<float>();
        public float[] L1 { get; init; } = Array.Empty<float>();
        public float[] B1 { get; init; } = Array.Empty<float>();
        public float[] L2 { get; init; } = Array.Empty<float>();
        public float[] B2 { get; init; } = Array.Empty<float>();
        public float[] L3 { get; init; } = Array.Empty<float>();
        public float[] B3 { get; init; } = Array.Empty<float>();
    }
}
