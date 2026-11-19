using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tiisu.DevTools;
using Xunit;

namespace Tiisu.DevTools.Tests;

/// <summary>
/// <c>--ab-ai</c>：默认不含 MCTS、追役臂、候选模型 wide 组、双巡目（discardTurn/drawTurn）、
/// 和牌时间分布 histogram、按座位 v2/候选权重路径、JSON/CSV/Markdown 输出。
/// </summary>
public sealed class AbAiEvalTests
{
    [Fact]
    public void Parse_默认不含mcts()
    {
        var options = DevCommands.ParseAbAiOptions(new[] { "--ab-ai" });

        Assert.Equal(100, options.Games);
        Assert.Equal(4, options.Mode);
        Assert.Equal(1u, options.StartSeed);
        Assert.False(options.Pursuit);
        Assert.False(options.PursuitArm);
        Assert.Null(options.V2Path);
        Assert.Null(options.CandidatePath);
        Assert.Equal("balanced", options.Style);
        Assert.DoesNotContain("mcts", options.Variants);
        Assert.Equal(new[] { "baseline", "simple", "normal" }, options.Variants);
    }

    [Fact]
    public void Parse_提供v2与候选路径时默认自动加入对应组()
    {
        string dir = TempDir();
        try
        {
            string v2 = Path.Combine(dir, "tiny_v2.json");
            string candidate = Path.Combine(dir, "tiny_candidate.json");
            WriteTinyWeights(v2, features: 181, hidden: 4);
            WriteTinyWeights(candidate, features: 339, hidden: 8);

            var options = DevCommands.ParseAbAiOptions(new[]
            {
                "--ab-ai", "--v2", v2, "--candidate", candidate,
            });

            Assert.Equal(new[] { "baseline", "v2", "wide", "simple", "normal" }, options.Variants);
            Assert.Equal(Path.GetFullPath(candidate), Path.GetFullPath(options.CandidatePath!));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Parse_追役臂()
    {
        var viaFlag = DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--pursuit-arm" });
        Assert.Contains("pursuit", viaFlag.Variants);
        Assert.True(viaFlag.PursuitArm);
        Assert.Equal(new[] { "baseline", "pursuit", "simple", "normal" }, viaFlag.Variants);

        var viaVariants = DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "baseline,pursuit" });
        Assert.Equal(new[] { "baseline", "pursuit" }, viaVariants.Variants);

        // 显式 --variants + --pursuit-arm：保证追役臂在场。
        var mixed = DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "baseline", "--pursuit-arm" });
        Assert.Equal(new[] { "baseline", "pursuit" }, mixed.Variants);
    }

    [Fact]
    public void Parse_变体别名与规范顺序_mcts最后()
    {
        string dir = TempDir();
        try
        {
            string v2 = Path.Combine(dir, "tiny_v2.json");
            string candidate = Path.Combine(dir, "tiny_candidate.json");
            WriteTinyWeights(v2, features: 181, hidden: 4);
            WriteTinyWeights(candidate, features: 339, hidden: 8);

            var alias = DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "simple,v4" });
            Assert.Equal(new[] { "baseline", "simple" }, alias.Variants);

            var all = DevCommands.ParseAbAiOptions(new[]
            {
                "--ab-ai", "--variants", "all,mcts", "--v2", v2, "--candidate", candidate,
            });
            Assert.Equal(
                new[] { "baseline", "pursuit", "v2", "wide", "simple", "normal", "mcts" },
                all.Variants);
            Assert.Equal("mcts", all.Variants[^1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Parse_非法参数抛异常()
    {
        string dir = TempDir();
        try
        {
            string candidate = Path.Combine(dir, "tiny_candidate.json");
            WriteTinyWeights(candidate, features: 339, hidden: 8);

            Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "v2" }));
            Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "v2", "--v2", "no-such-file.json" }));
            Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "wide" }));
            Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "wide", "--candidate", "no-such-file.json" }));
            Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "nope" }));
            Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--style", "nope" }));
            Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--games", "0" }));
            Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--what" }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DrawTurn_口径为ceil_83减墙_除以4()
    {
        Assert.Equal(0, DevCommands.AbAiDrawTurn(83));
        Assert.Equal(1, DevCommands.AbAiDrawTurn(82));
        Assert.Equal(6, DevCommands.AbAiDrawTurn(60));
        Assert.Equal(21, DevCommands.AbAiDrawTurn(0));   // 荒牌/海底：摸完 83 张
        Assert.Equal(21, DevCommands.AbAiDrawTurn(-5));  // 防御性 clamp
    }

    [Fact]
    public void Histogram_按巡目桶累计和牌率与流局率()
    {
        var histogram = DevCommands.AbAiBuildTurnHistogram(
            winTurns: new[] { 3, 9, 15, 20 },
            flowTurns: new[] { 21, 21 },
            totalRounds: 10);

        Assert.Equal(4, histogram.Count);
        Assert.Equal(new[] { "1-6", "7-12", "13-18", "19+" }, histogram.Select(b => b.Bucket).ToArray());
        Assert.Equal(1, histogram[0].Wins);
        Assert.Equal(0.1, histogram[0].CumulativeWinRate, 9);
        Assert.Equal(2, histogram[1].CumulativeWins);
        Assert.Equal(0.2, histogram[1].CumulativeWinRate, 9);
        Assert.Equal(3, histogram[2].CumulativeWins);
        Assert.Equal(4, histogram[3].CumulativeWins);
        Assert.Equal(0.4, histogram[3].CumulativeWinRate, 9);
        Assert.Equal(0, histogram[0].CumulativeFlows);
        Assert.Equal(2, histogram[3].CumulativeFlows);
        Assert.Equal(0.2, histogram[3].CumulativeFlowRate, 9);
    }

    [Fact]
    public void Run_追役臂与候选模型小样本_字段真实出现()
    {
        string dir = TempDir();
        try
        {
            string candidate = Path.Combine(dir, "tiny_candidate.json");
            WriteTinyWeights(candidate, features: 339, hidden: 8);

            var options = new DevCommands.AbAiOptions(
                Games: 1,
                Variants: new[] { "baseline", "pursuit", "wide" },
                V2Path: null,
                CandidatePath: candidate,
                PursuitArm: false,
                Pursuit: false,
                Style: "balanced",
                Mode: 1,
                StartSeed: 1,
                OutDirectory: dir,
                Quiet: true);

            var report = DevCommands.RunAbAi(options);

            Assert.Equal(new[] { "baseline", "pursuit", "wide" }, report.Variants.Select(v => v.Variant).ToArray());
            foreach (var variant in report.Variants)
            {
                Assert.False(variant.Isolated);
                Assert.Equal(1, variant.Games);
                Assert.NotEmpty(variant.RoundRows);
                Assert.Equal(4, variant.Histogram.Count);
                Assert.InRange(variant.DiscardTurnP90, 0, 100);
                Assert.InRange(variant.DrawTurnP90, 0, 100);
                Assert.All(variant.RoundRows, row =>
                {
                    Assert.Equal((row.TotalDiscards + 3) / 4, row.DiscardTurn);
                    Assert.Equal(DevCommands.AbAiDrawTurn(row.Wall), row.DrawTurn);
                });
            }

            var baseline = report.Variants.Single(v => v.Variant == "baseline");
            Assert.Equal(339, baseline.ModelFeatures);
            Assert.Equal(128, baseline.ModelHidden);
            Assert.Equal(3, baseline.ModelLayers);
            Assert.Contains("policy_weights_v4.json", baseline.ModelPath!);
            Assert.True(baseline.PursuitEntries >= 0);

            var pursuit = report.Variants.Single(v => v.Variant == "pursuit");
            Assert.Equal(339, pursuit.ModelFeatures);

            var wide = report.Variants.Single(v => v.Variant == "wide");
            Assert.Equal(339, wide.ModelFeatures);
            Assert.Equal(8, wide.ModelHidden);
            Assert.Equal(Path.GetFullPath(candidate), Path.GetFullPath(wide.ModelPath!));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Run_焦点座位按seed轮换0到3()
    {
        var options = new DevCommands.AbAiOptions(
            Games: 4,
            Variants: new[] { "baseline" },
            V2Path: null,
            CandidatePath: null,
            PursuitArm: false,
            Pursuit: false,
            Style: "balanced",
            Mode: 1,
            StartSeed: 100,
            OutDirectory: Path.Combine(Path.GetTempPath(), "tiisu-ab-unused"),
            Quiet: true);

        var report = DevCommands.RunAbAi(options);

        var variant = Assert.Single(report.Variants);
        Assert.Equal(new[] { 0, 1, 2, 3 }, variant.GameRows.Select(g => g.Seat).ToArray());
        Assert.Equal(new uint[] { 100, 101, 102, 103 }, variant.GameRows.Select(g => g.Seed).ToArray());
    }

    [Fact]
    public void Run_v2组_按座位路径加载v2并带模型元数据()
    {
        string dir = TempDir();
        try
        {
            string v2 = Path.Combine(dir, "tiny_v2.json");
            WriteTinyWeights(v2, features: 181, hidden: 4);

            var options = new DevCommands.AbAiOptions(
                Games: 1,
                Variants: new[] { "baseline", "v2" },
                V2Path: v2,
                CandidatePath: null,
                PursuitArm: false,
                Pursuit: false,
                Style: "balanced",
                Mode: 1,
                StartSeed: 3,
                OutDirectory: dir,
                Quiet: true);

            var report = DevCommands.RunAbAi(options);

            Assert.Equal(2, report.Variants.Count);
            var v2Report = report.Variants.Single(v => v.Variant == "v2");
            Assert.Equal(181, v2Report.ModelFeatures);
            Assert.Equal(4, v2Report.ModelHidden);
            Assert.Equal(Path.GetFullPath(v2), Path.GetFullPath(v2Report.ModelPath!));
            foreach (var variant in report.Variants)
            {
                Assert.True(variant.GameRows[0].NeuralDecisions > 0, $"{variant.Variant} 应走策略网");
                Assert.NotEmpty(variant.RoundRows);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Run_mcts隔离标记()
    {
        var options = new DevCommands.AbAiOptions(
            Games: 1,
            Variants: new[] { "baseline", "mcts" },
            V2Path: null,
            CandidatePath: null,
            PursuitArm: false,
            Pursuit: false,
            Style: "balanced",
            Mode: 1,
            StartSeed: 2,
            OutDirectory: Path.Combine(Path.GetTempPath(), "tiisu-ab-unused"),
            Quiet: true);

        var report = DevCommands.RunAbAi(options);

        Assert.False(report.Variants.Single(v => v.Variant == "baseline").Isolated);
        Assert.True(report.Variants.Single(v => v.Variant == "mcts").Isolated);
        Assert.Contains("200ms/步", report.MctsNote);
    }

    [Fact]
    public void Write_产出JSON与CSV与Markdown_含双巡目与histogram()
    {
        string dir = TempDir();
        try
        {
            var options = new DevCommands.AbAiOptions(
                Games: 1,
                Variants: new[] { "baseline" },
                V2Path: null,
                CandidatePath: null,
                PursuitArm: false,
                Pursuit: false,
                Style: "steady",
                Mode: 1,
                StartSeed: 1,
                OutDirectory: dir,
                Quiet: true);

            var report = DevCommands.RunAbAi(options);
            var (json, csv, md) = DevCommands.WriteAbAiReport(report, dir);

            Assert.True(File.Exists(json));
            Assert.True(File.Exists(csv));
            Assert.True(File.Exists(md));

            var node = JsonNode.Parse(File.ReadAllText(json))!.AsObject();
            Assert.Contains("ceil", node["turnDefinition"]!.GetValue<string>());
            Assert.Contains("drawTurn", node["turnDefinition"]!.GetValue<string>());
            Assert.Contains("200ms/步", node["config"]!["mctsNote"]!.GetValue<string>());
            var variant = node["variants"]!.AsArray()[0]!.AsObject();
            Assert.NotNull(variant["discardTurn"]!["mean"]);
            Assert.NotNull(variant["discardTurn"]!["p90"]);
            Assert.NotNull(variant["drawTurn"]!["mean"]);
            Assert.NotNull(variant["drawTurn"]!["p90"]);
            Assert.NotNull(variant["flowRate"]);
            Assert.Equal(4, variant["histogram"]!.AsArray().Count);
            Assert.NotNull(variant["histogram"]![0]!["cumulativeWinRate"]);
            Assert.NotNull(variant["histogram"]![0]!["cumulativeFlowRate"]);
            Assert.False((bool)variant["isolated"]!);
            Assert.Equal(339, (int)variant["model"]!["features"]!);
            Assert.Equal(128, (int)variant["model"]!["hidden"]!);

            var csvLines = File.ReadAllLines(csv);
            Assert.Equal(2, csvLines.Length);
            Assert.StartsWith("variant,isolated,games,rounds", csvLines[0]);
            Assert.Contains("discardTurnMean", csvLines[0]);
            Assert.Contains("drawTurnMean", csvLines[0]);
            Assert.Contains("flowRate", csvLines[0]);
            Assert.Contains("modelFeatures", csvLines[0]);

            string markdown = File.ReadAllText(md);
            Assert.Contains("和牌巡目", markdown);
            Assert.Contains("和牌时间分布", markdown);
            Assert.Contains("drawTurn", markdown);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Percentile_线性插值_双精度与整数版本()
    {
        var doubles = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var ints = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        Assert.Equal(0.0, DevCommands.Percentile(Array.Empty<double>(), 0.5));
        Assert.Equal(5.5, DevCommands.Percentile(doubles, 0.5), 9);
        Assert.Equal(9.1, DevCommands.Percentile(doubles, 0.9), 9);
        Assert.Equal(5.5, DevCommands.Percentile(ints, 0.5), 9);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-ab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>最小可解析权重：矩阵全 0，输出层 bias 指定牌种；features/hidden 可指定。</summary>
    private static void WriteTinyWeights(string path, int features, int hidden)
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
        b3[0] = 10f;

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

        File.WriteAllText(path, JsonSerializer.Serialize(payload), new UTF8Encoding(false));
    }
}