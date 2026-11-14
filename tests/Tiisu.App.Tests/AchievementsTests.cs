using System.Text.Json.Nodes;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class AchievementsTests
{
    [Fact]
    public void From_UnlocksByStatsThresholds()
    {
        var summary = StatsSummary.From(new JsonObject
        {
            ["rounds"] = 12,
            ["wins"] = 10,
            ["tsumoWins"] = 4,
            ["ronWins"] = 6,
            ["drawRounds"] = 2,
            ["tiisuinCount"] = 1,
            ["witnessCount"] = 3,
        });

        var items = Achievements.From(summary);

        Assert.Equal(7, Achievements.UnlockedCount(items));
        Assert.Contains(items, a => a.Id == "ten-rounds" && a.Unlocked);
        Assert.Contains(items, a => a.Id == "ten-wins" && a.Unlocked);
        Assert.Contains(items, a => a.Id == "tiisuin" && a.Unlocked);
        Assert.Contains(items, a => a.Id == "witness" && a.Unlocked);
        Assert.DoesNotContain(items, a => a.Id == "hundred-wins" && a.Unlocked);
    }
}
