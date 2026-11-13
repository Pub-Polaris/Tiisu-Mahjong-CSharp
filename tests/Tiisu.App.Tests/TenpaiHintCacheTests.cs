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

}
