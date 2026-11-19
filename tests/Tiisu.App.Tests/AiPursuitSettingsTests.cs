using System.Text.Json.Nodes;
using Tiisu.App.Runtime;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;
using Tiisu.Localization;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>7Neuu 自动追役原型：设置持久化 / 预设 / 语言 key / 未开启时行为不变。</summary>
public sealed class AiPursuitSettingsTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-pursuit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save再Load_追役开关与五项权重保持()
    {
        string dir = TempDir();
        try
        {
            var settings = new Settings
            {
                AiPursuitEnabled = true,
                AiStyle = new AiStyle
                {
                    Attack = 1.6,
                    Defense = 0.4,
                    Meld = 0.3,
                    Value = 1.8,
                    Pursuit = 1.1,
                },
            };

            settings.Save(dir);
            var loaded = Settings.Load(dir);

            Assert.True(loaded.AiPursuitEnabled);
            Assert.Equal(1.6, loaded.AiStyle.Attack, 6);
            Assert.Equal(0.4, loaded.AiStyle.Defense, 6);
            Assert.Equal(0.3, loaded.AiStyle.Meld, 6);
            Assert.Equal(1.8, loaded.AiStyle.Value, 6);
            Assert.Equal(1.1, loaded.AiStyle.Pursuit, 6);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_写出camelCase的aiPursuitEnabled与aiStyle()
    {
        string dir = TempDir();
        try
        {
            new Settings { AiPursuitEnabled = true, AiStyle = AiStyle.PursuitFocused }.Save(dir);

            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "settings.json")))!.AsObject();
            Assert.True((bool)json["aiPursuitEnabled"]!);
            var style = json["aiStyle"]!.AsObject();
            Assert.Equal(1.3, (double)style["attack"]!, 6);
            Assert.Equal(0.7, (double)style["defense"]!, 6);
            Assert.Equal(1.7, (double)style["pursuit"]!, 6);
            Assert.False(style.ContainsKey("Attack"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 旧存档缺字段_追役默认关闭且风格中性()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "settings.json"),
                "{\"thinkSeconds\": 9, \"language\": \"zh-CN\"}");

            var loaded = Settings.Load(dir);

            Assert.Equal(9, loaded.ThinkSeconds);
            Assert.False(loaded.AiPursuitEnabled);
            Assert.Equal(AiStyle.Default, loaded.AiStyle);
            Assert.Equal(1, loaded.AiStyle.MatchingPreset());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 坏权重被夹到0到2()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "settings.json"),
                "{\"aiStyle\": {\"attack\": 5.0, \"defense\": -3.0, \"meld\": 1.5, \"value\": 0.0, \"pursuit\": 2.0}}");

            var loaded = Settings.Load(dir);

            Assert.Equal(2.0, loaded.AiStyle.Attack, 6);
            Assert.Equal(0.0, loaded.AiStyle.Defense, 6);
            Assert.Equal(1.5, loaded.AiStyle.Meld, 6);
            Assert.Equal(0.0, loaded.AiStyle.Value, 6);
            Assert.Equal(2.0, loaded.AiStyle.Pursuit, 6);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Clone与CopyFrom_都带上追役字段()
    {
        var original = new Settings
        {
            AiPursuitEnabled = true,
            AiStyle = AiStyle.PursuitFocused,
        };

        var clone = original.Clone();
        Assert.True(clone.AiPursuitEnabled);
        Assert.Equal(AiStyle.PursuitFocused, clone.AiStyle);

        // 不可变 record：改原实例不影响副本，也不需要深拷贝。
        original.AiStyle = AiStyle.Steady;
        Assert.Equal(AiStyle.PursuitFocused, clone.AiStyle);

        var target = new Settings();
        target.CopyFrom(original);
        Assert.True(target.AiPursuitEnabled);
        Assert.Equal(AiStyle.Steady, target.AiStyle);
    }

    [Fact]
    public void 三个预设_都在0到2且可被匹配()
    {
        Assert.Equal(3, AiStyle.Presets.Count);
        Assert.Equal(1, AiStyle.Default.MatchingPreset());
        Assert.Equal(0, AiStyle.Steady.MatchingPreset());
        Assert.Equal(2, AiStyle.PursuitFocused.MatchingPreset());

        foreach (var preset in AiStyle.Presets)
        {
            Assert.True(preset.IsInRange);
            Assert.Equal(preset, preset.Clamped());
        }

        var custom = AiStyle.Default with { Attack = 1.05 };
        Assert.Equal(-1, custom.MatchingPreset());
    }

    [Fact]
    public void 语言key_三种语言都有()
    {
        var keys = new[]
        {
            "app.ui.settings.row.aiPursuit",
            "app.ui.settings.row.aiStylePreset",
            "app.ui.settings.row.aiStyleAttack",
            "app.ui.settings.row.aiStyleDefense",
            "app.ui.settings.row.aiStyleMeld",
            "app.ui.settings.row.aiStyleValue",
            "app.ui.settings.row.aiStylePursuit",
            "app.ui.settings.aiStyle.steady",
            "app.ui.settings.aiStyle.balanced",
            "app.ui.settings.aiStyle.pursuit",
            "app.ui.settings.aiStyle.custom",
        };

        // 直接从内嵌语言资源校验，避免改全局 L 语言影响并行运行的 UiTextTests。
        var assembly = typeof(L).Assembly;
        foreach (string language in L.SupportedLanguages)
        {
            string resource = $"Tiisu.Localization.lang.{language}.json";
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);

            var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(JsonNode.Parse(stream!), string.Empty, catalog);
            foreach (string key in keys)
            {
                Assert.True(catalog.ContainsKey(key), $"{language} 缺 key: {key}");
                Assert.False(string.IsNullOrWhiteSpace(catalog[key]), $"{language} 的 key 为空: {key}");
            }
        }
    }

    private static void Flatten(JsonNode? node, string prefix, Dictionary<string, string> target)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                Flatten(pair.Value, prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key, target);
            }

            return;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out string? text) && prefix.Length > 0)
        {
            target[prefix] = text ?? string.Empty;
        }
    }

    [Fact]
    public void 未开启追役_7Neuu整手牌事件流与旧设置完全一致()
    {
        // 四家全部锁 7Neuu；同一 seed 下，未开启追役时事件流必须逐位相同，
        // 即使玩家把五项风格权重调得很激进也不影响（普通弃牌仍 argmax）。
        var baseline = new Settings
        {
            AiSeatLocks = new[] { 4, 4, 4, 4 },
        };

        var aggressiveButOff = new Settings
        {
            AiSeatLocks = new[] { 4, 4, 4, 4 },
            AiPursuitEnabled = false,
            AiStyle = new AiStyle
            {
                Attack = 2.0,
                Defense = 0.0,
                Meld = 2.0,
                Value = 2.0,
                Pursuit = 2.0,
            },
        };

        var first = PlayOneHand(baseline, seed: 20261119);
        var second = PlayOneHand(aggressiveButOff, seed: 20261119);

        Assert.All(first.AiLevels, level => Assert.Equal(AiLevel.Neural, level));
        Assert.Equal(first.EventHash, second.EventHash);
        Assert.Equal(0, first.AiPursuitEntries);
        Assert.Equal(0, second.AiPursuitEntries);
        Assert.Equal(0, second.AiPursuitDecisions);
    }

    [Fact]
    public void 开启追役_四家全7Neuu_整手牌能跑完并进入过追役()
    {
        var settings = new Settings
        {
            AiSeatLocks = new[] { 4, 4, 4, 4 },
            AiPursuitEnabled = true,
            AiStyle = AiStyle.PursuitFocused,
        };

        int entries = 0;
        int decisions = 0;
        for (uint seed = 1; seed <= 16 && entries == 0; seed++)
        {
            var runtime = PlayOneHand(settings, seed);
            Assert.All(runtime.AiLevels, level => Assert.Equal(AiLevel.Neural, level));
            Assert.Equal(0, runtime.AuditIssueCount);
            entries += runtime.AiPursuitEntries;
            decisions += runtime.AiPursuitDecisions;
        }

        Assert.True(entries > 0, $"多手牌内应至少进入一次追役（decisions={decisions}）");
        Assert.True(decisions > 0, "进入追役后应至少有一次追役状态下的弃牌决策");
    }

    private static MatchRuntime PlayOneHand(Settings settings, uint seed)
    {
        var runtime = new MatchRuntime(
            settings,
            new Mulberry32Random(seed),
            _ => { },
            saveStore: null,
            stats: null,
            roundLog: null,
            matchSeed: seed)
        {
            AllSeatsAi = true,
        };

        runtime.StartMatch(4);
        return runtime;
    }
}
