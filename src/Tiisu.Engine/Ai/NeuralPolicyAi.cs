using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tiisu.Engine.Ai;

/// <summary>
/// 小策略网推理（P2-1）：42 维局面特征 → 34 类分数，取手牌内最高分。
/// 权重由 <c>ml/train_policy.py</c> 导出；文件缺失时返回 -1，调用方回退 <see cref="TieredAi"/>。
/// </summary>
public static class NeuralPolicyAi
{
    private const int FeatureCount = 42;

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
            float[] x = BuildFeatures(ctx);
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

    private static float[] BuildFeatures(AiDecisionContext ctx)
    {
        var x = new float[FeatureCount];
        foreach (var tile in ctx.Hand)
        {
            int idx = TileIndex(tile);
            if (idx >= 0)
            {
                x[idx] += 1;
            }
        }

        x[34] = Math.Min(ctx.WallCount, 83) / 83f;
        x[35] = Math.Min(ctx.Discards.Count, 18) / 18f;
        x[36] = Math.Min(ctx.Honba, 8) / 8f;
        x[37] = ctx.SeatWind == SeatWind.East ? 1f : 0f;
        int seat = (int)ctx.SeatWind;
        if (seat is 1 or 2 or 3)
        {
            x[37 + seat] = 1f;
        }

        int pairs = 0;
        for (int i = 0; i < 34; i++)
        {
            if (x[i] >= 2)
            {
                pairs++;
            }
        }

        x[41] = Math.Min(pairs, 7) / 7f;
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
        string? env = Environment.GetEnvironmentVariable("TIISU_POLICY_WEIGHTS");
        if (!string.IsNullOrEmpty(env))
        {
            yield return env!;
        }

        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Assets", "ml", "policy_weights.json");
        yield return Path.Combine(baseDir, "policy_weights.json");

        // 开发期：从 bin 目录往上找仓库里的 ml/
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir is not null; i++)
        {
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
        public float[] L1 { get; init; } = Array.Empty<float>();
        public float[] B1 { get; init; } = Array.Empty<float>();
        public float[] L2 { get; init; } = Array.Empty<float>();
        public float[] B2 { get; init; } = Array.Empty<float>();
        public float[] L3 { get; init; } = Array.Empty<float>();
        public float[] B3 { get; init; } = Array.Empty<float>();
    }
}
