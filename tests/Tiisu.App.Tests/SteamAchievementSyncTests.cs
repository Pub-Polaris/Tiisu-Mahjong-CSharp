using System.Collections.Generic;
using System.Linq;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class SteamAchievementSyncTests
{
    private sealed class FakeSteam : ISteamService
    {
        public bool IsAvailable { get; init; } = true;
        public List<string> Unlocked { get; } = new();
        public bool TryInitialize() => true;
        public void Shutdown() { }
        public bool UnlockAchievement(string steamId)
        {
            Unlocked.Add(steamId);
            return true;
        }

        public bool StoreStat(string statId, int value) => true;
    }

    [Fact]
    public void Report_OnlyUnlockedAndMapped_Once()
    {
        var steam = new FakeSteam();
        var sync = new SteamAchievementSync(steam);
        var achievements = new[]
        {
            new Achievement("first-win", "首胜", "", "1/1", true),
            new Achievement("ten-wins", "十胜", "", "3/10", false),
            new Achievement("unknown", "未知", "", "0/1", true),
        };

        var first = sync.Report(achievements);
        var second = sync.Report(achievements);

        Assert.Equal(new[] { "ACH_FIRST_WIN" }, first);
        Assert.Empty(second);
        Assert.Equal(new[] { "ACH_FIRST_WIN" }, steam.Unlocked);
    }

    [Fact]
    public void Report_Unavailable_IsNoOp()
    {
        var steam = new FakeSteam { IsAvailable = false };
        var sync = new SteamAchievementSync(steam);

        var sent = sync.Report(new[] { new Achievement("first-win", "首胜", "", "1/1", true) });

        Assert.Empty(sent);
        Assert.Empty(steam.Unlocked);
    }
}
