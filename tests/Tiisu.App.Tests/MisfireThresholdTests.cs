using Tiisu.App.Runtime;
using Tiisu.Core;
using Tiisu.Engine;
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

    private static MatchRuntime RuntimeWithOptional() => new(
        new Settings { OptionalYaku = new List<string> { "triple_even_ko", "double_even_ko" } },
        new Mulberry32Random(9),
        _ => { },
        saveStore: null,
        stats: null,
        roundLog: null,
        matchSeed: 9);

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

    [Fact]
    public void 荣和错和赋按门槛口径返回()
    {
        // 3 副露 + 暗手 m2m2m2 z9，等 z9 成对：对对和 + 缺七 = 4（不足门槛），
        // 三双逢(4) 只是加分役 → TotalFu=8 ≥ 7，但 ThresholdFu=4，必须判错和。
        var runtime = RuntimeWithOptional();
        var player = runtime.Engine.Players[0];
        player.IsCalled = true;
        player.Hand.AddRange(new[] { Tile.Parse("m2"), Tile.Parse("m2"), Tile.Parse("m2"), Tile.Parse("z9") });
        player.Melds.Add(new Tiisu.Engine.Meld(MeldType.Pon, new[] { Tile.Parse("s1"), Tile.Parse("s1"), Tile.Parse("s1") }));
        player.Melds.Add(new Tiisu.Engine.Meld(MeldType.Pon, new[] { Tile.Parse("p4"), Tile.Parse("p4"), Tile.Parse("p4") }));
        player.Melds.Add(new Tiisu.Engine.Meld(MeldType.Pon, new[] { Tile.Parse("p6"), Tile.Parse("p6"), Tile.Parse("p6") }));

        int fu = runtime.RonMisfireFu(0, Tile.Parse("z9"));

        Assert.InRange(fu, 1, 6);
    }
}
