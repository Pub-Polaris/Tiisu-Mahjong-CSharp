using System.Text;
using System.Text.Json;
using Tiisu.Core;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;

namespace Tiisu.Engine.Tests;

/// <summary>
/// A/B 评测所需的能力：策略网按座位/路径加载权重（不做全局 override）、默认 v4 不受影响、
/// 动作分布熵按座位统计。
/// </summary>
public sealed class NeuralPolicyAiAbTests
{
    [Fact]
    public void 按路径权重_同一进程内不同路径互不影响()
    {
        string dir = TempDir();
        try
        {
            // 两个 tiny 权重：所有矩阵为 0，只靠输出层 bias 指定 argmax 牌种。
            string pathA = Path.Combine(dir, "tiny_a.json");
            string pathB = Path.Combine(dir, "tiny_b.json");
            WriteTinyWeights(pathA, features: 181, hidden: 4, bestTileIndex: 0);
            WriteTinyWeights(pathB, features: 181, hidden: 4, bestTileIndex: 1);

            var ctx = Context(Tiles("1m2m3m4m5m6m7m8m9m1p1p1p2p3p"));

            int a = NeuralPolicyAi.ChooseDiscard(ctx, pathA);
            int b = NeuralPolicyAi.ChooseDiscard(ctx, pathB);

            Assert.Equal(new Tile(Suit.Man, 1), ctx.Hand[a]);
            Assert.Equal(new Tile(Suit.Man, 2), ctx.Hand[b]);
            Assert.NotEqual(a, b);

            var scoresA = NeuralPolicyAi.ScoreDiscards(ctx, pathA);
            Assert.NotEmpty(scoresA);
            var oneMan = scoresA.Single(s => s.Tile == new Tile(Suit.Man, 1));
            var twoMan = scoresA.Single(s => s.Tile == new Tile(Suit.Man, 2));
            Assert.True(oneMan.Logit > twoMan.Logit);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 按路径权重_默认v4不被影响()
    {
        Assert.True(NeuralPolicyAi.Available, "测试环境应能找到 ml/policy_weights_v4.json");
        string defaultBefore = NeuralPolicyAi.LoadedFrom!;
        Assert.Contains("policy_weights_v4.json", defaultBefore);

        string dir = TempDir();
        try
        {
            string tiny = Path.Combine(dir, "tiny_v2.json");
            WriteTinyWeights(tiny, features: 181, hidden: 4, bestTileIndex: 1);

            var ctx = Context(Tiles("1m2m3m4m5m6m7m8m9m1p1p1p2p3p"));
            int before = NeuralPolicyAi.ChooseDiscard(ctx);

            int fromTiny = NeuralPolicyAi.ChooseDiscard(ctx, tiny);
            int after = NeuralPolicyAi.ChooseDiscard(ctx);

            Assert.Equal(before, after);
            Assert.Equal(defaultBefore, NeuralPolicyAi.LoadedFrom);
            Assert.NotEqual(-1, fromTiny);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 显式路径缺失或非法_抛异常而不是静默回退()
    {
        var ctx = Context(Tiles("1m2m3m4m5m6m7m8m9m1p1p1p2p3p"));

        Assert.Throws<InvalidOperationException>(() =>
            NeuralPolicyAi.ChooseDiscard(ctx, Path.Combine(Path.GetTempPath(), "tiisu-nope-" + Guid.NewGuid().ToString("N") + ".json")));

        string dir = TempDir();
        try
        {
            string broken = Path.Combine(dir, "broken.json");
            File.WriteAllText(broken, "{\"features\": 181, \"layers\": []}");
            Assert.Throws<InvalidOperationException>(() => NeuralPolicyAi.ChooseDiscard(ctx, broken));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 动作分布熵_按座位统计并可清零()
    {
        var ctx = Context(Tiles("1m2m3m4m5m6m7m8m9m1p1p1p2p3p")) with { PlayerIndex = 2 };

        try
        {
            NeuralPolicyAi.ResetDiagnostics();
            NeuralPolicyAi.CollectDiagnostics = true;

            Assert.Equal(0, NeuralPolicyAi.DiagnosticsDecisionsForPlayer(2));
            Assert.NotEmpty(NeuralPolicyAi.ScoreDiscards(ctx));

            Assert.True(NeuralPolicyAi.DiagnosticsDecisionsForPlayer(2) >= 1);
            double entropy = NeuralPolicyAi.DiagnosticsAverageEntropyForPlayer(2);
            Assert.InRange(entropy, 0.0, Math.Log(34) + 1e-9);
            Assert.True(entropy > 0.0);

            NeuralPolicyAi.ResetDiagnostics();
            Assert.Equal(0, NeuralPolicyAi.DiagnosticsDecisionsForPlayer(2));
        }
        finally
        {
            NeuralPolicyAi.CollectDiagnostics = false;
            NeuralPolicyAi.ResetDiagnostics();
        }
    }

    [Fact]
    public void 熵函数_均匀分布为lnN_尖锐分布接近0()
    {
        double uniform = NeuralPolicyAi.EntropyOf(new float[] { 0, 0, 0, 0 });
        Assert.Equal(Math.Log(4), uniform, 9);

        double peaked = NeuralPolicyAi.EntropyOf(new float[] { 100, 0, 0, 0 });
        Assert.True(peaked < 1e-6);

        Assert.Equal(0.0, NeuralPolicyAi.EntropyOf(Array.Empty<float>()));
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-neural-ab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>写一个最小可解析权重：矩阵全 0，仅输出层 bias 在指定牌种上置 10。</summary>
    private static void WriteTinyWeights(string path, int features, int hidden, int bestTileIndex)
    {
        var w1 = new float[hidden][];
        for (int r = 0; r < hidden; r++)
        {
            w1[r] = new float[features];
        }

        var b1 = new float[hidden];
        var w2 = new float[hidden][];
        for (int r = 0; r < hidden; r++)
        {
            w2[r] = new float[hidden];
        }

        var b2 = new float[hidden];
        var w3 = new float[34][];
        for (int r = 0; r < 34; r++)
        {
            w3[r] = new float[hidden];
        }

        var b3 = new float[34];
        b3[bestTileIndex] = 10f;

        var payload = new
        {
            version = 4,
            features,
            hidden,
            layers = new object[]
            {
                new { w = w1, b = b1 },
                new { w = w2, b = b2 },
                new { w = w3, b = b3 },
            },
            feature_mean = Array.Empty<float>(),
            feature_std = Array.Empty<float>(),
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload),
            new UTF8Encoding(false));
    }

    private static AiDecisionContext Context(List<Tile> hand) =>
        new(
            hand,
            Array.Empty<Meld>(),
            Array.Empty<Tile>(),
            NoRivers(),
            Array.Empty<Meld>(),
            0,
            70,
            new[] { 25000, 25000, 25000, 25000 },
            SeatWind.East,
            SeatWind.East,
            0,
            0);

    private static IReadOnlyList<IReadOnlyList<Tile>> NoRivers() =>
        new[] { new List<Tile>(), new List<Tile>(), new List<Tile>(), new List<Tile>() };

    private static List<Tile> Tiles(string spec)
    {
        var result = new List<Tile>();
        var digits = new List<char>();
        foreach (char c in spec)
        {
            if (char.IsDigit(c))
            {
                digits.Add(c);
                continue;
            }

            if (!char.IsLetter(c))
            {
                throw new FormatException("非法牌串: " + spec);
            }

            char suit = char.ToLowerInvariant(c);
            foreach (char digit in digits)
            {
                result.Add(Tile.Parse(suit + digit.ToString()));
            }

            digits.Clear();
        }

        if (digits.Count > 0)
        {
            throw new FormatException("牌串末尾缺少花色: " + spec);
        }

        return result;
    }
}
