using System.Text;
using System.Text.Json;
using Tiisu.App.Runtime;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>
/// A/B 评测所需的运行时能力：RoundStat（含巡目口径）、焦点座位覆盖（level / 策略路径 / MCTS / 追役）
/// 只改该座位、默认 v4 生产行为不变。
/// </summary>
public sealed class AbAiRuntimeTests
{
    [Fact]
    public void RoundStat_每局一条_巡目与弃牌口径自洽()
    {
        var runtime = PlayMatch(new Settings { AiSeatLocks = new[] { 4, 4, 4, 4 } }, seed: 7, mode: 1);

        var stat = Assert.Single(runtime.RoundStats);
        Assert.Equal(stat.DiscardsBySeat.Sum(), stat.TotalDiscards);
        Assert.Equal((stat.TotalDiscards + 3) / 4, stat.Turn);
        Assert.Equal(4, stat.DiscardsBySeat.Length);
        Assert.Equal(4, stat.MeldsBySeat.Length);
        Assert.Equal(4, stat.ScoresAfter.Length);
        Assert.InRange(stat.Wall, 0, 70);
        Assert.Equal(stat.Winners.Count, stat.WinCount);
        Assert.Contains(stat.Kind, new[] { "tsumo", "ron", "tiisuin", "flow", "special_flow" });

        if (stat.Kind is "flow" or "special_flow")
        {
            Assert.Empty(stat.Winners);
            Assert.Empty(stat.Payers);
        }

        if (stat.Winners.Count > 0 && stat.Kind is "ron")
        {
            Assert.NotEmpty(stat.Payers);
        }

        if (stat.Kind is "tsumo" or "ron")
        {
            Assert.NotEmpty(stat.YakuNames);
        }

        Assert.True(runtime.Events.Count > 0, "RoundStat 是附加数据，不应影响原事件流");
    }

    [Fact]
    public void RoundStat_开新场时清空()
    {
        var runtime = PlayMatch(new Settings { AiSeatLocks = new[] { 4, 4, 4, 4 } }, seed: 9, mode: 1);
        Assert.Single(runtime.RoundStats);

        // 再开一场：计数先清空，只保留新场已结束的那一局。
        runtime.StartMatch(1);
        Assert.Single(runtime.RoundStats);
    }

    [Fact]
    public void 焦点覆盖_只改该座位档位_其余三家仍7Neuu()
    {
        var settings = new Settings { AiSeatLocks = new[] { 4, 4, 4, 4 } };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(11), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 11)
        {
            AllSeatsAi = true,
            AiAbOverride = new MatchRuntime.AiSeatAbOverride(
                2, AiLevel.Simple, PolicyWeightsPath: null, PursuitEnabled: false, AiStyle.Default, MctsEnabled: false),
        };

        runtime.StartMatch(1);

        Assert.Equal(AiLevel.Simple, runtime.AiLevels[2]);
        for (int seat = 0; seat < 4; seat++)
        {
            if (seat != 2)
            {
                Assert.Equal(AiLevel.Neural, runtime.AiLevels[seat]);
            }
        }
    }

    [Fact]
    public void 焦点覆盖_v2权重路径_不影响其余座位且能跑完整局()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-ab-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string v2 = Path.Combine(dir, "tiny_v2.json");
            WriteTinyV2Weights(v2);

            var settings = new Settings { AiSeatLocks = new[] { 4, 4, 4, 4 } };
            var runtime = new MatchRuntime(settings, new Mulberry32Random(13), _ => { },
                saveStore: null, stats: null, roundLog: null, matchSeed: 13)
            {
                AllSeatsAi = true,
                AiAbOverride = new MatchRuntime.AiSeatAbOverride(
                    0, AiLevel.Neural, v2, PursuitEnabled: false, AiStyle.Default, MctsEnabled: false),
            };

            runtime.StartMatch(1);

            Assert.Equal(AiLevel.Neural, runtime.AiLevels[0]);
            for (int seat = 1; seat < 4; seat++)
            {
                Assert.Equal(AiLevel.Neural, runtime.AiLevels[seat]);
            }

            Assert.True(runtime.NeuralDecisions > 0, "焦点座位应走策略网");
            Assert.NotEmpty(runtime.RoundStats);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 焦点覆盖_MCTS_Shura加MctsEnabled能跑完整局()
    {
        var settings = new Settings { AiSeatLocks = new[] { 4, 4, 4, 4 } };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(17), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 17)
        {
            AllSeatsAi = true,
            AiAbOverride = new MatchRuntime.AiSeatAbOverride(
                1, AiLevel.Shura, PolicyWeightsPath: null, PursuitEnabled: false, AiStyle.Default, MctsEnabled: true),
        };

        runtime.StartMatch(1);

        Assert.Equal(AiLevel.Shura, runtime.AiLevels[1]);
        Assert.NotEmpty(runtime.RoundStats);
    }

    [Fact]
    public void baseline覆盖等于生产v4_事件流逐位一致()
    {
        var settings = new Settings { AiSeatLocks = new[] { 4, 4, 4, 4 } };

        var production = new MatchRuntime(settings, new Mulberry32Random(23), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 23) { AllSeatsAi = true };
        production.StartMatch(1);

        var baseline = new MatchRuntime(settings, new Mulberry32Random(23), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 23)
        {
            AllSeatsAi = true,
            AiAbOverride = new MatchRuntime.AiSeatAbOverride(
                0, AiLevel.Neural, PolicyWeightsPath: null, PursuitEnabled: false, AiStyle.Default, MctsEnabled: false),
        };
        baseline.StartMatch(1);

        Assert.Equal(production.EventHash, baseline.EventHash);
        Assert.Equal(production.RoundStats.Count, baseline.RoundStats.Count);
    }

    private static MatchRuntime PlayMatch(Settings settings, uint seed, int mode)
    {
        var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: seed)
        {
            AllSeatsAi = true,
        };

        runtime.StartMatch(mode);
        int guard = 0;
        while (runtime.Phase != MatchPhase.MatchOver && guard++ < 100)
        {
            if (runtime.Phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
            }
        }

        return runtime;
    }

    /// <summary>写一个最小可解析的 v2（181 维）权重，隐藏层 4，用于验证按座位路径加载。</summary>
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
