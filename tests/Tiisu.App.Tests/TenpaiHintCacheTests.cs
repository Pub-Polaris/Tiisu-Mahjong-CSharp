using Tiisu.App.Runtime;
using Tiisu.Core;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class TenpaiHintCacheTests
{
    [Fact]
    public void SameState_ReusesCachedList()
    {
        var runtime = new MatchRuntime(new Settings { RecordTiles = false }, new Mulberry32Random(7), _ => { },
            null, null, null, 7) { AutoPilot = true };
        runtime.StartMatch(4);
        var engine = runtime.Engine;
        var player = engine.Players[runtime.HumanIndex];
        var settings = new Settings { Ruleset = Ruleset.Qizi };
        var cache = new TenpaiHintCache();

        var first = cache.Get(engine, player, settings);
        var second = cache.Get(engine, player, settings);

        Assert.Same(first, second);
    }

    [Fact]
    public void HandChange_InvalidatesCache()
    {
        var runtime = new MatchRuntime(new Settings { RecordTiles = false }, new Mulberry32Random(8), _ => { },
            null, null, null, 8) { AutoPilot = true };
        runtime.StartMatch(4);
        var engine = runtime.Engine;
        var player = engine.Players[runtime.HumanIndex];
        var settings = new Settings { Ruleset = Ruleset.Qizi };
        var cache = new TenpaiHintCache();

        var first = cache.Get(engine, player, settings);
        player.Hand.Add(new Tile(Suit.Man, 1));
        player.Hand.Sort();
        var second = cache.Get(engine, player, settings);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void OptionalYakuChange_InvalidatesCache()
    {
        var runtime = new MatchRuntime(new Settings { RecordTiles = false }, new Mulberry32Random(9), _ => { },
            null, null, null, 9) { AutoPilot = true };
        runtime.StartMatch(4);
        var engine = runtime.Engine;
        var player = engine.Players[runtime.HumanIndex];
        var cache = new TenpaiHintCache();

        var first = cache.Get(engine, player, new Settings { Ruleset = Ruleset.Qizi });
        var second = cache.Get(engine, player, new Settings
        {
            Ruleset = Ruleset.Qizi,
            OptionalYaku = new List<string> { "all_number" },
        });

        Assert.NotSame(first, second);
    }
}
