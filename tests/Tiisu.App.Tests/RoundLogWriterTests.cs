using System.Text.Json;
using System.Text.Json.Nodes;
using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Deal;
using Tiisu.Engine.Random;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class RoundLogWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tiisu-roundlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    private static MahjongEngine NewEngine()
    {
        var yaku = YakuData.LoadDefault();
        var engine = new MahjongEngine(
            checker: new WinChecker(yaku),
            yakuData: yaku,
            randomSource: new Mulberry32Random(7),
            ruleset: Ruleset.Qizi);

        var hand = Enumerable.Range(1, 9).Select(n => new Tile(Suit.Man, n))
            .Concat(Enumerable.Range(1, 4).Select(n => new Tile(Suit.Pin, n)))
            .ToArray();
        var hands = new List<IReadOnlyList<Tile>>
        {
            hand, hand, hand, hand,
        };
        engine.StartNewRound(new RoundDeal(0, 0, hands, null));
        return engine;
    }

    [Fact]
    public void Flush_WritesRoundHeadersAndSteps()
    {
        var engine = NewEngine();
        var writer = new RoundLogWriter(_dir) { Enabled = true };

        writer.Step(engine, 0, "discard", new Tile(Suit.Man, 1));
        writer.Flush(engine, "test-session");

        Assert.True(File.Exists(writer.FilePath()));
        var lines = File.ReadAllLines(writer.FilePath());
        Assert.True(lines.Length >= 5, "至少 4 个 round 头 + 1 条步骤");

        var headers = lines.Take(4).Select(l => JsonNode.Parse(l)!).ToArray();
        foreach (var header in headers)
        {
            Assert.Equal("round", header["type"]!.GetValue<string>());
            Assert.Equal("test-session", header["session"]!.GetValue<string>());
            Assert.NotNull(header["name"]);
            Assert.NotNull(header["wind"]);
            Assert.NotNull(header["dealer"]);
            Assert.NotNull(header["round"]);
            Assert.NotNull(header["honba"]);
        }

        var step = lines.Skip(4).Select(l => JsonNode.Parse(l)!).First(n => n["a"] is not null);
        Assert.Equal("discard", step["a"]!.GetValue<string>());
        Assert.Equal(0, step["p"]!.GetValue<int>());
        Assert.Equal(JsonValueKind.String, step["hand"]!.GetValueKind());
        Assert.Equal(JsonValueKind.Array, step["melds"]!.GetValueKind());
        Assert.NotNull(step["h"]);
    }

    [Fact]
    public void Disabled_WritesNothing()
    {
        var engine = NewEngine();
        var writer = new RoundLogWriter(_dir) { Enabled = false };

        writer.Step(engine, 0, "discard", new Tile(Suit.Man, 1));
        writer.Flush(engine, "test-session");

        string logs = Path.Combine(_dir, "logs");
        Assert.True(!Directory.Exists(logs) || Directory.GetFiles(logs).Length == 0);
    }
}
