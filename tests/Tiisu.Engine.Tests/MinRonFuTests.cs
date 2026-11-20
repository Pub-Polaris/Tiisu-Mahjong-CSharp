using Tiisu.Core;
using Tiisu.Engine;
using Xunit;

namespace Tiisu.Engine.Tests;

/// <summary>听牌逐张最小荣和赋的核心口径。</summary>
public sealed class MinRonFuTests
{
    private static readonly WinChecker Checker = new(YakuData.LoadDefault());

    private static List<Tile> H(string spec)
    {
        var tiles = new List<Tile>();
        foreach (string part in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            char suitChar = part[^1];
            Suit suit = suitChar switch
            {
                'm' => Suit.Man,
                'p' => Suit.Pin,
                's' => Suit.Sou,
                'z' => Suit.Honor,
                _ => throw new ArgumentException(part, nameof(spec)),
            };
            foreach (char digit in part[..^1])
            {
                tiles.Add(new Tile(suit, digit - '0'));
            }
        }

        return tiles;
    }

    [Fact]
    public void 多拆法_最低赋不高于默认最高赋()
    {
        var hand = H("11122233344455m");
        var max = Checker.CheckWin(hand, selfDraw: false);
        var min = Checker.CheckWin(hand, selfDraw: false, preferLowestFu: true);

        Assert.True(max.Success, max.Reason);
        Assert.True(min.Success, min.Reason);
        Assert.True(min.TotalFu <= max.TotalFu);
    }

    [Fact]
    public void 门前自摸手_荣和最小赋为空()
    {
        var options = new TenpaiOptions
        {
            SelfDraw = true,
            IncludeMinRonFu = true,
            HasCalled = false,
            SeatWind = SeatWind.East,
            RoundWind = SeatWind.East,
            BaseFu = 7,
        };
        var entries = TenpaiCalculator.ListTenpai(Checker, H("123m 456m 789m 234p 5s"), Array.Empty<Tiisu.Core.Meld>(), options);
        var wait = Assert.Single(entries, e => e.Tile == new Tile(Suit.Sou, 5));
        Assert.True(wait.Success);
        Assert.Null(wait.MinRonFu);
    }

    [Fact]
    public void 普通荣和手_有最小赋()
    {
        var options = new TenpaiOptions
        {
            SelfDraw = true,
            IncludeMinRonFu = true,
            HasCalled = false,
            SeatWind = SeatWind.East,
            RoundWind = SeatWind.East,
            BaseFu = 7,
        };
        var entries = TenpaiCalculator.ListTenpai(Checker, H("1112223334445m"), Array.Empty<Tiisu.Core.Meld>(), options);
        var wait = Assert.Single(entries, e => e.Tile == new Tile(Suit.Man, 5));
        Assert.True(wait.Success);
        Assert.True(wait.MinRonFu is > 0);
        Assert.True(wait.MaxRonFu is > 0);
        Assert.True(wait.MinRonFu <= wait.MaxRonFu);
    }
}
