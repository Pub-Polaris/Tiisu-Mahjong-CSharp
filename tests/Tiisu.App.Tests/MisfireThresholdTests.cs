using Tiisu.App.Runtime;
using Tiisu.Core;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>
/// 错和罚必须与判和同一门槛口径（复核 261112 遗留 #1）：
/// TotalFu 含可选役、ThresholdFu 不含；够不够门槛只看 ThresholdFu。
/// </summary>
public sealed class MisfireThresholdTests
{
    private static MatchRuntime NewRuntime() => new(
        new Settings(),
        new Mulberry32Random(7),
        _ => { },
        saveStore: null,
        stats: null,
        roundLog: null,
        matchSeed: 7);

    [Fact]
    public void 可选役抬过总赋但门槛不足_仍算错和()
    {
        var runtime = NewRuntime();
        var player = runtime.Engine.Players[0];
        player.IsCalled = true;

        var result = new WinResult
        {
            Success = false,
            Reason = "no_yaku",
            TotalFu = 9,
            ThresholdFu = 5,   // 可选役 4 赋不算门槛
        };

        Assert.True(runtime.IsMisfireResult(player, result));
    }

    [Fact]
    public void 门槛本身够_不算错和()
    {
        var runtime = NewRuntime();
        var player = runtime.Engine.Players[0];
        player.IsCalled = true;

        var result = new WinResult
        {
            Success = false,
            Reason = "no_yaku",
            TotalFu = 9,
            ThresholdFu = 7,
        };

        Assert.False(runtime.IsMisfireResult(player, result));
    }

    [Fact]
    public void 未报听不判错和()
    {
        var runtime = NewRuntime();
        var player = runtime.Engine.Players[0];
        player.IsCalled = false;

        var result = new WinResult
        {
            Success = false,
            Reason = "no_yaku",
            TotalFu = 9,
            ThresholdFu = 5,
        };

        Assert.False(runtime.IsMisfireResult(player, result));
    }
}
