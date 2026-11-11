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

        // RestoreCallState 生效后，StartNewRound 会把「下局禁和」轮转成本局禁和、清掉下局标记。
        // 反过来说：这三条断言能证明存档里的禁和标记确实进了引擎，不是被丢掉的。
        Assert.Equal(new[] { false, false, false, true }, runtime.Engine.CallWinBanned);
        Assert.All(runtime.Engine.CallWinBannedNextHand, banned => Assert.False(banned));
        Assert.All(runtime.Engine.Players, player => Assert.False(player.IsCalled));
    }
}
