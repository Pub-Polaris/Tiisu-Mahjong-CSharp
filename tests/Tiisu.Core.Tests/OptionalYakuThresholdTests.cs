using Tiisu.Core;
using Xunit;

namespace Tiisu.Core.Tests;

/// <summary>
/// 可选役只加分、不参与起和线（2026-11-12 裁定 / 复核第十节）。
/// 用例取自旧 golden 第 104 例：基础赋 3（缺七 1 + 自摸 2）+ 三双逢 4 = 7，
/// 旧实现放行，新口径必须判 no_yaku。
/// </summary>
public sealed class OptionalYakuThresholdTests
{
    private static readonly WinChecker AllOptional = new(YakuData.LoadDefault());
    private static readonly WinChecker NoOptional = new(YakuData.LoadDefault(), Array.Empty<string>());

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
                _ => throw new ArgumentException("bad tile: " + part),
            };
            foreach (char digit in part[..^1])
            {
                tiles.Add(new Tile(suit, digit - '0'));
            }
        }

        return tiles;
    }

    private static Meld M(string spec)
    {
        int colon = spec.IndexOf(':');
        return new Meld(spec[..colon], H(spec[(colon + 1)..]));
    }

    [Fact]
    public void 可选役不能把不够门槛的牌抬过线()
    {
        var hand = H("1p 2p 3p 5z 5z");
        var melds = new[]
        {
            M("pon:2m 2m 2m"),
            M("pon:4p 4p 4p"),
            M("pon:6s 6s 6s"),
        };

        var withOptional = AllOptional.CheckWin(
            hand,
            selfDraw: true,
            hasCalled: true,
            melds: melds,
            winTile: new Tile(Suit.Pin, 1));

        Assert.False(withOptional.Success);
        Assert.Equal("no_yaku", withOptional.Reason);
        // 赋里仍然算了可选役（只是不参与门槛判断）
        Assert.Contains(withOptional.ActiveYaku, y => y.Name == "三双逢");
        Assert.True(withOptional.TotalFu >= 7);
        Assert.True(withOptional.ThresholdFu < 7);

        var withoutOptional = NoOptional.CheckWin(
            hand,
            selfDraw: true,
            hasCalled: true,
            melds: melds,
            winTile: new Tile(Suit.Pin, 1));

        Assert.False(withoutOptional.Success);
        Assert.DoesNotContain(withoutOptional.ActiveYaku, y => y.Name == "三双逢");
        Assert.True(withoutOptional.TotalFu < withOptional.TotalFu);
        Assert.Equal(withoutOptional.TotalFu, withoutOptional.ThresholdFu);
    }

    [Fact]
    public void 门槛已够时可选役只改总赋()
    {
        // 三暗刻 5 + 门前自摸 4 = 9（已过 7 赋），三双逢 4 只影响 TotalFu。
        var hand = H("2m 2m 2m 4p 4p 4p 6s 6s 6s 7m 8m 9m 1z 1z");

        var withOptional = AllOptional.CheckWin(hand, selfDraw: true);
        var withoutOptional = NoOptional.CheckWin(hand, selfDraw: true);

        Assert.True(withOptional.Success);
        Assert.True(withoutOptional.Success);
        Assert.Equal(withoutOptional.ThresholdFu, withOptional.ThresholdFu);
        Assert.Equal(withoutOptional.TotalFu + 4, withOptional.TotalFu);
    }
}
