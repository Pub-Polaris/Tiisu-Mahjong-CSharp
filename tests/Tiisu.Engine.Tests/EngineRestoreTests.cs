using Tiisu.Engine;
using Xunit;

namespace Tiisu.Engine.Tests;

/// <summary>宿主读档恢复场况（审计 P3-13：不再反射写私有字段）。</summary>
public sealed class EngineRestoreTests
{
    [Fact]
    public void RestoreProgress_写回场况与点数()
    {
        var engine = new MahjongEngine();

        engine.RestoreProgress(
            handsPlayed: 7,
            handNumber: 5,
            dealerIndex: 2,
            honba: 3,
            roundWindIdx: 1,
            dealerCount: 4,
            scores: new[] { 51000, 49000, 47000, 45000 });

        Assert.Equal(7, engine.HandsPlayed);
        Assert.Equal(5, engine.HandNumber);
        Assert.Equal(2, engine.DealerIndex);
        Assert.Equal(3, engine.Honba);
        Assert.Equal(1, engine.RoundWindIdx);
        Assert.Equal(4, engine.DealerCount);
        Assert.Equal(new[] { 51000, 49000, 47000, 45000 }, engine.Scores);
    }

    [Fact]
    public void RestoreProgress_越界值被夹紧()
    {
        var engine = new MahjongEngine();

        engine.RestoreProgress(-1, -2, 99, -3, -4, -5);

        Assert.Equal(0, engine.HandsPlayed);
        Assert.Equal(0, engine.HandNumber);
        Assert.Equal(MahjongEngine.PlayerCount - 1, engine.DealerIndex);
        Assert.Equal(0, engine.Honba);
        Assert.Equal(0, engine.RoundWindIdx);
        Assert.Equal(0, engine.DealerCount);
    }
}
