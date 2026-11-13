using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tiisu.Engine.SelfPlay;

namespace Tiisu.Engine.Ai;

/// <summary>
/// 小策略网推理（P2-1）：支持 v2（181 维）与 v4（339 维，自博弈特征）权重。
/// 权重由 <c>ml/train_policy.py</c> 导出；文件缺失时返回 -1，调用方回退 <see cref="TieredAi"/>。
/// </summary>
public static class NeuralPolicyAi
{
    private const int V2FeatureCount = 181;

    private static Weights? _weights;
    private static bool _loadTried;
    private static string? _loadedFrom;

    /// <summary>当前权重来源（调试用）。</summary>
    public static string? LoadedFrom => _loadedFrom;

    public static bool Available
    {
        get
        {
            EnsureLoaded();
            return _weights is not null;
        }
    }

    /// <summary>返回弃牌在 <see cref="AiDecisionContext.Hand"/> 里的下标；无权重返回 -1。</summary>
    public static int ChooseDiscard(AiDecisionContext ctx)
    {
        EnsureLoaded();
        if (_weights is { } w)
        {
            float[] x = Normalize(BuildFeatures(ctx, w.Features), w);
            float[] h1 = Dense(w.L1, w.B1, x, w.Hidden);
            float[] h2 = Dense(w.L2, w.B2, h1, w.Hidden);
            float[] logits = Dense(w.L3, w.B3, h2, 34);

            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < ctx.Hand.Count; i++)
            {
                int idx = TileIndex(ctx.Hand[i]);
                if (idx < 0)
                {
                    continue;
                }

                float s = logits[idx];
                if (s > bestScore)
                {
                    bestScore = s;
                    best = i;
                }
            }

            return best;
        }

        return -1;
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

    private static void EnsureLoaded()
    {
        if (_loadTried)
        {
            return;
        }

        _loadTried = true;
        foreach (string path in CandidatePaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node is not JsonObject obj || obj["layers"] is not JsonArray layers || layers.Count < 3)
                {
                    continue;
                }

                _weights = new Weights
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
                _loadedFrom = path;
                return;
            }
            catch
            {
                // 换下一个候选路径
            }
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
