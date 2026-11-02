using System.Collections.Generic;
using Tiisu.Core;
using Tiisu.Engine;
using CoreMeld = Tiisu.Core.Meld;

namespace Tiisu.Engine.Tests;

/// <summary>1102工程：推倒胡规则集在引擎层的剥离行为。</summary>
public sealed class TuidaoHuRulesetTests
{
    private static List<Tile> H(string spec)
    {
        var tiles = new List<Tile>();
        foreach (string part in spec.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
        {
            char suitChar = part[^1];
            Suit suit = suitChar switch
            {
                'm' => Suit.Man,
                'p' => Suit.Pin,
                's' => Suit.Sou,
                'z' => Suit.Honor,
                _ => throw new System.ArgumentException(part),
            };
            foreach (char digit in part[..^1])
            {
                tiles.Add(new Tile(suit, digit - '0'));
            }
        }

        return tiles;
    }

    [Fact]
    public void 推倒胡_判役器只看形态_赋为0()
    {
        var checker = new WinChecker(YakuData.LoadDefault(), ruleset: Ruleset.TuidaoHu);
        var hand = H("123m 456m 789p 234s 55z");

        var result = checker.CheckWin(hand, selfDraw: false, hasCalled: false, ankanCount: 0, minkanCount: 0,
            melds: System.Array.Empty<CoreMeld>(), firstTurn: false, winTile: null, context: null);

        Assert.True(result.Success);
        Assert.Equal(0, result.TotalFu);
        Assert.Single(result.ActiveYaku);
        Assert.Equal("四顺一对", result.ActiveYaku[0].Name);
    }

    [Fact]
    public void 推倒胡_不成形态就不算和()
    {
        var checker = new WinChecker(YakuData.LoadDefault(), ruleset: Ruleset.TuidaoHu);
        var hand = H("123m 456m 789p 234s 5z 6z");

        var result = checker.CheckWin(hand, selfDraw: false, hasCalled: false, ankanCount: 0, minkanCount: 0,
            melds: System.Array.Empty<CoreMeld>(), firstTurn: false, winTile: null, context: null);

        Assert.False(result.Success);
    }

    [Fact]
    public void 推倒胡_没有报听()
    {
        var engine = new MahjongEngine(ruleset: Ruleset.TuidaoHu);
        engine.SetMode(4);

        Assert.Equal(Ruleset.TuidaoHu, engine.Ruleset);
        Assert.False(engine.CanDeclareCall(engine.Players[0]));
    }

    [Fact]
    public void 推倒胡_和牌不结算点数()
    {
        var engine = new MahjongEngine(ruleset: Ruleset.TuidaoHu);
        engine.SetMode(4);
        var before = engine.Scores.ToArray();

        engine.DeclareWin(0, selfDraw: false, new WinSettlement { TotalFu = 0 }, discarderIndex: 1);

        Assert.Equal(before, engine.Scores.ToArray());
    }
}
