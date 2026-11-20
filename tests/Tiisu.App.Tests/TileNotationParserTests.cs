using Tiisu.App.Ui.Markdown;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class TileNotationParserTests
{
    private static IReadOnlyList<TileRun> Parse(string text)
    {
        Assert.True(TileNotationParser.TryParse(text, out var runs), text);
        return runs;
    }

    [Fact]
    public void CompactHonors_ExpandToSixTiles()
    {
        var runs = Parse("123z456z");
        Assert.Equal(6, runs.Count(r => r.Id is not null));
        Assert.Equal(new[] { "1z", "2z", "3z", "4z", "5z", "6z" },
            runs.Where(r => r.Id is not null).Select(r => r.Id));
    }

    [Fact]
    public void GroupedAndCompactForms_AreEquivalent()
    {
        var compact = Parse("112233m445566p556677z");
        var expanded = Parse("11m22m33m44p55p66p55z66z77z");

        Assert.Equal(compact.Count(r => r.Id is not null), expanded.Count(r => r.Id is not null));
        Assert.Equal(compact.Where(r => r.Id is not null).Select(r => r.Id),
            expanded.Where(r => r.Id is not null).Select(r => r.Id));
    }

    [Fact]
    public void Underscore_AddsTwoPixelsBeforeNextTile()
    {
        var runs = Parse("11m_22m");
        var tiles = runs.Where(r => r.Id is not null).ToArray();
        Assert.Equal(4, tiles.Length);
        Assert.Equal(2f, tiles[2].ExtraGapBefore);
    }

    [Fact]
    public void AmbiguousSixTileShape_IsOnlyAMultiset()
    {
        var runs = Parse("123333p");
        Assert.Equal(6, runs.Count(r => r.Id is not null));
        Assert.Equal(4, runs.Count(r => r.Id == "3p"));
        Assert.DoesNotContain(runs, r => r.Id is null);
    }

    [Theory]
    [InlineData("c123m")]
    [InlineData("b777z")]
    [InlineData("a222z")]
    [InlineData("k333p")]
    public void MeldTokens_Parse(string text)
    {
        var runs = Parse(text);
        Assert.NotEmpty(runs);
        Assert.All(runs, r => Assert.NotNull(r.Id));
    }

    [Theory]
    [InlineData("c123")]
    [InlineData("k333")]
    [InlineData("1238z")]
    [InlineData("11111m")]
    [InlineData("0m")]
    [InlineData("123x")]
    public void InvalidNotation_IsRejected(string text)
    {
        Assert.False(TileNotationParser.TryParse(text, out _), text);
    }
}
