using Tiisu.App.Runtime;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>读档恢复走强类型 API（复核 P3-13），不是反射。</summary>
public sealed class MatchResumeTests
{
    [Fact]
    public void Resume_恢复场况与点数()
    {
        var runtime = new MatchRuntime(
            new Settings(),
            new Mulberry32Random(11),
            _ => { },
            saveStore: null,
            stats: null,
            roundLog: null,
            matchSeed: 11);

        var save = new SaveState
        {
            InProgress = true,
            ModeTarget = 4,
            HandsPlayed = 3,
            HandNumber = 2,
            Scores = new[] { 51000, 49000, 47000, 45000 },
            DealerIndex = 2,
            Honba = 2,
            RoundWindIdx = 1,
            DealerCount = 3,
            IsCalled = new[] { false, true, false, false },
            CallWinBanned = new[] { false, false, true, false },
            CallWinBannedNextHand = new[] { false, false, false, true },
        };

        runtime.Resume(save);

        Assert.Equal(3, runtime.Engine.HandsPlayed);
        Assert.Equal(2, runtime.Engine.DealerIndex);
        Assert.Equal(2, runtime.Engine.Honba);
        Assert.Equal(1, runtime.Engine.RoundWindIdx);
        Assert.Equal(3, runtime.Engine.DealerCount);
        Assert.Equal(new[] { 51000, 49000, 47000, 45000 }, runtime.Engine.Scores);
    }
}
