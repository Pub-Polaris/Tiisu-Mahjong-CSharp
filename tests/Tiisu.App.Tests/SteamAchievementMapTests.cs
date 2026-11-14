using System.Linq;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class SteamAchievementMapTests
{
    [Fact]
    public void Map_CoversEveryLocalAchievementOnce()
    {
        var local = Achievements.From(StatsSummary.From(new System.Text.Json.Nodes.JsonObject()))
            .Select(a => a.Id)
            .ToArray();

        Assert.Equal(local.Length, SteamAchievementMap.Items.Count);
        Assert.Equal(
            local.OrderBy(x => x),
            SteamAchievementMap.Items.Select(x => x.LocalId).OrderBy(x => x));
        Assert.Equal(SteamAchievementMap.Items.Count,
            SteamAchievementMap.Items.Select(x => x.SteamId).Distinct().Count());
    }

    [Fact]
    public void SteamIdFor_ReturnsNullForUnknown()
    {
        Assert.Equal("ACH_FIRST_WIN", SteamAchievementMap.SteamIdFor("first-win"));
        Assert.Null(SteamAchievementMap.SteamIdFor("missing"));
        Assert.Equal(new[] { "ACH_FIRST_WIN" }, SteamAchievementMap.SteamIdsFor(new[] { "first-win", "missing" }));
    }
}
