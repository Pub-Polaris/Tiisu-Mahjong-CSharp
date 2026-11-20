using Tiisu.Core;
using Tiisu.Engine;
using Xunit;

namespace Tiisu.Engine.Tests;

/// <summary>吃牌来源的引擎级不变式：只能吃上家。</summary>
public sealed class ChiSourceTests
{
    private static List<Tile> Hand(string spec)
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

    private static MahjongEngine NewRound()
    {
        var engine = new MahjongEngine();
        var hands = new List<IReadOnlyList<Tile>>
        {
            Hand("234m 456m 789m 123p 5s"),
            Hand("1234m 6789p 12345s"),
            Hand("6789m 1234p 6789s 1z"),
            Hand("2345m 6789p 2345s 1z"),
        };
        engine.StartNewRound(new RoundDeal(0, 0, hands));
        return engine;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Chi_FromNonPreviousPlayer_IsRejectedByEngine(int discarder)
    {
        var engine = NewRound();
        var oneMan = new Tile(Suit.Man, 1);
        var trio = Hand("123m");

        // 玩家 0 的上家是 3；1 / 2 都不是上家，不允许吃。
        var ex = Assert.Throws<InvalidOperationException>(() =>
            engine.ApplyMeld(0, MeldType.Chi, oneMan, discarder, trio));
        Assert.Contains("上家", ex.Message);
    }

    [Fact]
    public void Chi_FromPreviousPlayer_IsAcceptedByEngine()
    {
        var engine = NewRound();
        var oneMan = new Tile(Suit.Man, 1);
        var trio = Hand("123m");

        engine.ApplyMeld(0, MeldType.Chi, oneMan, 3, trio);
        Assert.Contains(engine.Players[0].Melds, m => m.Type == MeldType.Chi);
    }
}
