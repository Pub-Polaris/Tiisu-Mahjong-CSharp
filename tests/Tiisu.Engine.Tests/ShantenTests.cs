using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Ai;

namespace Tiisu.Engine.Tests;

/// <summary>
/// 向听 / 进张手工牌例。牌例全部是「暗手」，副露只通过 meldCount 传入。
/// </summary>
public sealed class ShantenTests
{
    [Theory]
    // 标准形：和牌 / 听牌 / 一向听 / 两向听
    [InlineData("123m456m789m111p22z", 0, 0, -1, ShantenKind.Standard)]
    [InlineData("123m456m789m111p2z", 0, 0, 0, ShantenKind.Standard)]
    [InlineData("123m456m789m11p23s", 0, 0, 0, ShantenKind.Standard)]
    [InlineData("123m456m789m11p2s5s", 0, 0, 1, ShantenKind.Standard)]
    [InlineData("123m456m11p23s55z7z", 0, 0, 1, ShantenKind.Standard)]
    [InlineData("123m456m11p23s5z7z9s", 0, 0, 2, ShantenKind.Standard)]
    // 14 张的「4 面子 + 搭子」不能算 -1：打一张后才是听牌
    [InlineData("123m456m789m111p23s", 0, 0, 0, ShantenKind.Standard)]
    // 七对子（四张同牌按 2 对）
    [InlineData("11z22z33z44z55z66z7z", 0, 0, 0, ShantenKind.SevenPairs)]
    [InlineData("11z22z33z44z55z6z8z", 0, 0, 1, ShantenKind.SevenPairs)]
    [InlineData("1111z22z33z44z55z6z", 0, 0, 0, ShantenKind.SevenPairs)]
    // 国士
    [InlineData("19m19p19s1234567z", 0, 0, 0, ShantenKind.Kokushi)]
    [InlineData("119m19p19s123456z", 0, 0, 0, ShantenKind.Kokushi)]
    [InlineData("1199m19p19s12345z", 0, 0, 1, ShantenKind.Kokushi)]
    // 副露 / 杠：每副露算一个已完成面子
    [InlineData("123m456m789m1p", 1, 0, 0, ShantenKind.Standard)]
    [InlineData("123m456m1p", 2, 0, 0, ShantenKind.Standard)]
    [InlineData("123m45m1p9p", 2, 0, 1, ShantenKind.Standard)]
    [InlineData("123m456m789m1p", 1, 1, 0, ShantenKind.Standard)]
    public void Calculate_HandCases(string spec, int meldCount, int kanCount, int shanten, ShantenKind kind)
    {
        var hand = Tiles(spec);

        var result = Shanten.Calculate(hand, meldCount, kanCount);

        Assert.Equal(shanten, result.Shanten);
        Assert.Equal(kind, result.Kind);
    }

    [Fact]
    public void Calculate_WithMeldList_CountsKongAsOneSet()
    {
        var hand = Tiles("123m456m789m1p");
        var melds = new List<Meld>
        {
            new(MeldType.Minkan, Tiles("1111z")),
        };

        var result = Shanten.Calculate(hand, melds);

        Assert.Equal(0, result.Shanten);
        Assert.Equal(ShantenKind.Standard, result.Kind);
    }

    [Fact]
    public void Calculate_FourOfAKind_CountsAsTwoPairs()
    {
        // 1111z 按两对算：2 + 22/33/44/55 四对 = 6 对 => 七对子听牌
        var result = Shanten.Calculate(Tiles("1111z22z33z44z55z6z"));

        Assert.Equal(0, result.Shanten);
        Assert.Equal(ShantenKind.SevenPairs, result.Kind);
    }

    [Fact]
    public void Calculate_OpenHand_DisablesSevenPairsAndKokushi()
    {
        var closedKokushi = Shanten.Calculate(Tiles("19m19p19s1234567z"));
        var openHand = Shanten.Calculate(Tiles("123m456m789m1p"), 1);

        Assert.Equal(ShantenKind.Kokushi, closedKokushi.Kind);
        Assert.Equal(ShantenKind.Standard, openHand.Kind);
        Assert.Equal(0, openHand.Shanten);
    }

    private static List<Tile> Tiles(string spec)
    {
        var result = new List<Tile>();
        var digits = new List<char>();
        foreach (char c in spec)
        {
            if (char.IsDigit(c))
            {
                digits.Add(c);
                continue;
            }

            if (!char.IsLetter(c))
            {
                throw new FormatException("非法牌串: " + spec);
            }

            char suit = char.ToLowerInvariant(c);
            foreach (char digit in digits)
            {
                result.Add(Tile.Parse(suit + digit.ToString()));
            }

            digits.Clear();
        }

        if (digits.Count > 0)
        {
            throw new FormatException("牌串末尾缺少花色: " + spec);
        }

        return result;
    }
}
