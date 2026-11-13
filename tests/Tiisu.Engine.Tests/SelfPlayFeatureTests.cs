using Tiisu.Core;
using Tiisu.Engine.SelfPlay;
using Xunit;

namespace Tiisu.Engine.Tests;

public sealed class SelfPlayFeatureTests
{
    [Fact]
    public void FeatureLayout_HasDocumentedLength()
    {
        Assert.Equal(339, SelfPlayFeatures.Count);
        Assert.Equal(4, SelfPlayFeatures.Version);
        Assert.Equal(339, 327 + HandPatternFeatures.Count);
    }

    [Fact]
    public void EngineFeatures_AreFiniteAndInRange()
    {
        var engine = new MahjongEngine(ruleset: Ruleset.Qizi);
        var x = SelfPlayFeatures.Build(engine, 0);

        Assert.Equal(SelfPlayFeatures.Count, x.Length);
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
    }
}
