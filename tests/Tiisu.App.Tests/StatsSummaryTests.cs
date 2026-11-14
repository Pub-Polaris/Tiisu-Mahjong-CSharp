using System.Text.Json.Nodes;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class StatsSummaryTests
{
    [Fact]
    public void From_AggregatesAndSortsPlayers()
    {
        var json = new JsonObject
        {
            ["rounds"] = 10,
            ["wins"] = 4,
            ["tsumoWins"] = 1,
            ["ronWins"] = 3,
            ["drawRounds"] = 6,
            ["specialFlowRounds"] = 1,
            ["tiisuinCount"] = 1,
            ["witnessCount"] = 3,
            ["cumulativePoints"] = 100000,
            ["byPlayer"] = new JsonObject
            {
                ["北极星"] = new JsonObject
                {
                    ["wins"] = 3,
                    ["tiisuin"] = 1,
                    ["points"] = 70000,
                },
                ["AI"] = new JsonObject
                {
                    ["wins"] = 1,
                    ["witness"] = 3,
                    ["points"] = 30000,
                },
            },
        };

        var stats = StatsSummary.From(json);

        Assert.Equal(10, stats.Rounds);
        Assert.Equal(4, stats.Wins);
        Assert.Equal(0.4, stats.WinRate, 3);
        Assert.Equal(1.0 / 4, stats.TsumoRate, 3);
        Assert.Equal(0.6, stats.DrawRate, 3);
        Assert.Equal(2, stats.Players.Count);
        Assert.Equal("北极星", stats.Players[0].Name);
        Assert.Equal("AI", stats.Players[1].Name);
    }

    [Fact]
    public void From_EmptyIsZeroAndDoesNotDivideByZero()
    {
        var stats = StatsSummary.From(new JsonObject());

        Assert.Equal(0, stats.Rounds);
        Assert.Equal(0, stats.WinRate);
        Assert.Equal(0, stats.DrawRate);
        Assert.Empty(stats.Players);
    }
}
