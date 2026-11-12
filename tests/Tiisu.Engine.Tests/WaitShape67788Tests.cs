using Tiisu.Core;
using Xunit;

namespace Tiisu.Engine.Tests;

/// <summary>回归：67788 形听 6/9 的高目 6 不能判成不能和。</summary>
public sealed class WaitShape67788Tests
{
    private static Tile T(string wire)
    {
        var suit = wire[^1] switch
        {
            'm' => Suit.Man,
            'p' => Suit.Pin,
            's' => Suit.Sou,
            _ => Suit.Honor,
        };
        return new Tile(suit, wire[0] - '0');
    }

    private static List<Tile> Hand(params string[] wires) => wires.Select(T).OrderBy(t => t.Index).ToList();

    [Theory]
    [InlineData("6s")]
    [InlineData("9s")]
    public void HighAndLowWait_BothWin(string winTile)
    {
        var yaku = YakuData.LoadDefault();
        var checker = new WinChecker(yaku);
        var hand = Hand(
            "6s", "7s", "7s", "8s", "8s",
            "1s", "2s", "3s", "4s", "5s", "6s",
            "5s", "5s", winTile);

        var result = checker.CheckWin(
            hand,
            selfDraw: false,
            hasCalled: false,
            ankanCount: 0,
            minkanCount: 0,
            melds: null,
            firstTurn: false,
            winTile: T(winTile),
            context: new WinContext { SeatWind = "南", RoundWind = "東" });

        Assert.True(result.Success,
            winTile + " should win: reason=" + result.Reason + " fu=" + result.TotalFu
            + " threshold=" + result.ThresholdFu);
    }

    [Fact]
    public void ActualHand_23466m345p67788s_WinsOn6s()
    {
        var yaku = YakuData.LoadDefault();
        var checker = new WinChecker(yaku);
        var hand = Hand(
            "2m", "3m", "4m", "6m", "6m",
            "3p", "4p", "5p",
            "6s", "7s", "7s", "8s", "8s", "6s");

        var result = checker.CheckWin(
            hand,
            selfDraw: true,
            hasCalled: false,
            ankanCount: 0,
            minkanCount: 0,
            melds: null,
            firstTurn: false,
            winTile: T("6s"),
            context: new WinContext { SeatWind = "南", RoundWind = "東" });

        Assert.True(result.Success,
            "6s should win: reason=" + result.Reason + " fu=" + result.TotalFu
            + " threshold=" + result.ThresholdFu);
    }
}