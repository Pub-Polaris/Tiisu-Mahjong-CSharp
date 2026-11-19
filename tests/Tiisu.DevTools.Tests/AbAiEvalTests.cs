using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tiisu.DevTools;
using Xunit;

namespace Tiisu.DevTools.Tests;

/// <summary>
/// <c>--ab-ai</c> 五组 A/B：参数解析、焦点座位轮换、指标聚合（和牌巡目 mean/p50/p90）、
/// 按座位 v2 权重路径、JSON/CSV/Markdown 输出。
/// </summary>
public sealed class AbAiEvalTests
{
    [Fact]
    public void Parse_默认跳过v2组_并提供常用默认值()
    {
        var options = DevCommands.ParseAbAiOptions(new[] { "--ab-ai" });

        Assert.Equal(100, options.Games);
        Assert.Equal(4, options.Mode);
        Assert.Equal(1u, options.StartSeed);
        Assert.False(options.Pursuit);
        Assert.Equal("balanced", options.Style);
        Assert.Equal(new[] { "baseline", "mcts", "simple", "normal" }, options.Variants);
        Assert.Null(options.V2Path);
    }

    [Fact]
    public void Parse_提供v2路径时默认五组齐全()
    {
        string dir = TempDir();
        try
        {
            string v2 = Path.Combine(dir, "tiny_v2.json");
            WriteTinyV2Weights(v2);

            var options = DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--v2", v2 });

            Assert.Equal(new[] { "baseline", "v2", "mcts", "simple", "normal" }, options.Variants);
            Assert.Equal(Path.GetFullPath(v2), Path.GetFullPath(options.V2Path!));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Parse_变体别名与规范顺序()
    {
        string dir = TempDir();
        try
        {
            string v2 = Path.Combine(dir, "tiny_v2.json");
            WriteTinyV2Weights(v2);

            var alias = DevCommands.ParseAbAiOptions(new[]
            {
                "--ab-ai", "--variants", "simple,v4", "--v2", v2,
            });
            Assert.Equal(new[] { "baseline", "simple" }, alias.Variants);

            var all = DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "all", "--v2", v2 });
            Assert.Equal(new[] { "baseline", "v2", "mcts", "simple", "normal" }, all.Variants);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Parse_非法参数抛异常()
    {
        Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "v2" }));
        Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "v2", "--v2", "no-such-file.json" }));
        Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--variants", "nope" }));
        Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--style", "nope" }));
        Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--games", "0" }));
        Assert.Throws<ArgumentException>(() => DevCommands.ParseAbAiOptions(new[] { "--ab-ai", "--what" }));
    }

    [Fact]
    public void Parse_追役开关与风格预设()
    {
        var options = DevCommands.ParseAbAiOptions(new[]
        {
            "--ab-ai", "--variants", "baseline", "--pursuit", "--style", "pursuit", "--games", "3",
        });

        Assert.True(options.Pursuit);
        Assert.Equal("pursuit", options.Style);
        Assert.Equal(3, options.Games);
    }

    [Fact]
    public void Run_追役开关小样本可跑通()
    {
        var options = new DevCommands.AbAiOptions(
            Games: 1,
            Variants: new[] { "baseline" },
            V2Path: null,
            Pursuit: true,
            Style: "balanced",
            Mode: 1,
            StartSeed: 5,
            OutDirectory: Path.Combine(Path.GetTempPath(), "tiisu-ab-unused"),
            Quiet: true);

        var report = DevCommands.RunAbAi(options);

        Assert.True(report.Options.Pursuit);
        var variant = Assert.Single(report.Variants);
        Assert.Single(variant.GameRows);
        Assert.NotEmpty(variant.RoundRows);
    }

    [Fact]
    public void Run_两组小样本_指标结构自洽()
    {
        var options = new DevCommands.AbAiOptions(
            Games: 1,
            Variants: new[] { "baseline", "simple" },
            V2Path: null,
            Pursuit: false,
            Style: "balanced",
            Mode: 1,
            StartSeed: 1,
            OutDirectory: Path.Combine(Path.GetTempPath(), "tiisu-ab-unused"),
            Quiet: true);

        var report = DevCommands.RunAbAi(options);

        Assert.Equal(2, report.Variants.Count);
        Assert.Contains("和牌巡目", report.TurnDefinition);
        foreach (var variant in report.Variants)
        {
            Assert.Equal(1, variant.Games);
            Assert.True(variant.Rounds >= 1);
            Assert.Equal(variant.Rounds, variant.RoundRows.Count);
            Assert.Single(variant.GameRows);
            Assert.InRange(variant.AvgRank, 1.0, 4.0);
            Assert.InRange(variant.Top1Rate, 0.0, 1.0);
            Assert.InRange(variant.Top2Rate, 0.0, 1.0);
            Assert.InRange(variant.RoundWinRate, 0.0, 1.0);
            Assert.InRange(variant.DealInRate, 0.0, 1.0);
            Assert.True(variant.AvgWinnersPerRound >= 0);
            Assert.True(variant.WinTurnP50 >= variant.WinTurnMean - 10);
            Assert.True(variant.WinTurnP90 >= variant.WinTurnP50);
            Assert.True(variant.FocusWinTurnP90 >= variant.FocusWinTurnP50);
            Assert.All(variant.RoundRows, row =>
            {
                Assert.Equal((row.TotalDiscards + 3) / 4, row.Turn);
                Assert.InRange(row.Wall, 0, 70);
                Assert.Equal(4, row.ScoresAfter.Length);
                Assert.Equal(row.Winners.Length, row.Winners.Distinct().Count());
                Assert.InRange(row.Payers.Length, 0, 3);
            });
        }

        var baseline = report.Variants.Single(v => v.Variant == "baseline");
        Assert.Contains("v4", baseline.FocusDescription);
        var simple = report.Variants.Single(v => v.Variant == "simple");
        Assert.Contains("Simple", simple.FocusDescription);
    }

    [Fact]
    public void Run_焦点座位按seed轮换0到3()
    {
        var options = new DevCommands.AbAiOptions(
            Games: 4,
            Variants: new[] { "baseline" },
            V2Path: null,
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
    public void Run_v2组_按座位路径加载v2且不覆盖其余座位()
    {
        string dir = TempDir();
        try
        {
            string v2 = Path.Combine(dir, "tiny_v2.json");
            WriteTinyV2Weights(v2);

            var options = new DevCommands.AbAiOptions(
                Games: 1,
                Variants: new[] { "baseline", "v2" },
                V2Path: v2,
                Pursuit: false,
                Style: "balanced",
                Mode: 1,
                StartSeed: 3,
                OutDirectory: dir,
                Quiet: true);

            var report = DevCommands.RunAbAi(options);

            Assert.Equal(2, report.Variants.Count);
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
    public void Write_产出JSON与CSV与Markdown()
    {
        string dir = TempDir();
        try
        {
            var options = new DevCommands.AbAiOptions(
                Games: 1,
                Variants: new[] { "baseline" },
                V2Path: null,
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
            Assert.NotNull(node["turnDefinition"]);
            Assert.Contains("ceil", node["turnDefinition"]!.GetValue<string>());
            var variants = node["variants"]!.AsArray();
            Assert.Single(variants);
            Assert.NotNull(variants[0]!["winTurn"]);
            Assert.NotNull(variants[0]!["roundRows"]);

            var csvLines = File.ReadAllLines(csv);
            Assert.Equal(2, csvLines.Length);
            Assert.StartsWith("variant,games,rounds", csvLines[0]);
            Assert.Contains("winTurnMean", csvLines[0]);

            Assert.Contains("和牌巡目", File.ReadAllText(md));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Percentile_线性插值()
    {
        var values = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        Assert.Equal(0.0, DevCommands.Percentile(Array.Empty<double>(), 0.5));
        Assert.Equal(1.0, DevCommands.Percentile(values, 0.0));
        Assert.Equal(10.0, DevCommands.Percentile(values, 1.0));
        Assert.Equal(5.5, DevCommands.Percentile(values, 0.5), 9);
        Assert.Equal(9.1, DevCommands.Percentile(values, 0.9), 9);
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

    /// <summary>最小可解析的 v2（181 维）权重：矩阵全 0，输出层 bias 指定牌种。</summary>
    private static void WriteTinyV2Weights(string path)
    {
        const int features = 181;
        const int hidden = 4;
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
