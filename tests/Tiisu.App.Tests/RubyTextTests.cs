using System.Linq;
using Tiisu.App.Rendering;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class RubyTextTests
{
    [Fact]
    public void Parse_SplitsPlainAndRubySegments()
    {
        var segments = RubyText.Parse("東風{戦|せん}です");

        Assert.Equal(3, segments.Count);
        Assert.Equal(new RubySegment("東風", null), segments[0]);
        Assert.Equal(new RubySegment("戦", "せん"), segments[1]);
        Assert.Equal(new RubySegment("です", null), segments[2]);
    }

    [Fact]
    public void StripMarkup_LeavesBaseTextAndMalformedMarkup()
    {
        Assert.Equal("東風戦です", RubyText.StripMarkup("東風{戦|せん}です"));
        Assert.Equal("a{b}c", RubyText.StripMarkup("a{b}c"));
        Assert.False(RubyText.HasMarkup("東風戦"));
        Assert.True(RubyText.HasMarkup("{卯|う}"));
    }
}
