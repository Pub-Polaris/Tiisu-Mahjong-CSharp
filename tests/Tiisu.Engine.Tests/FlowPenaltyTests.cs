using Tiisu.Core;
using Tiisu.Engine;

namespace Tiisu.Engine.Tests;

/// <summary>
/// 荒牌流局罚项（规则书 4.4）：未听返杠 700/杠、鸣牌后无役听牌 1400/2100。
/// 「无役」= 一个役都数不出来（表外赋不算）；有 1 赋的役牌、断幺 2 赋都算有役 → 不罚。
/// </summary>
public sealed class FlowPenaltyTests
{
    private const int Start = 49000;

    /// <summary>非关注玩家的填充手牌：三组不同顺子 + 四张单牌，和不出形（不干扰罚项）。</summary>
    private static List<Tile> Filler() => Tiles("m2 m3 m4 p5 p6 p7 s1 s2 s3 z2 z3 z4 z6");

    private static List<Tile> Tiles(string spec) =>
        spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Tile.Parse).ToList();

    /// <summary>只给 1 号玩家特殊配牌，其余三家填充；庄家由参数指定。</summary>
    private static MahjongEngine NewEngine(int dealer, List<Tile> player1Hand)
    {
        var hands = new List<IReadOnlyList<Tile>>
        {
            Filler(),
            player1Hand,
            Filler(),
            Filler(),
        };

        var engine = new MahjongEngine();
        engine.SetMode(1);
        engine.HandNumber++;
        engine.StartNewRound(new RoundDeal(dealer, 8, hands));
        return engine;
    }

    private static void AssertScores(MahjongEngine engine, params int[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], engine.Scores[i]);
        }

        Assert.Equal(expected.Sum(), engine.Scores.Sum());
    }

    /// <summary>碰白（三元役牌 1 赋）→ 有役，即使总赋不到 7 起和线也不罚。</summary>
    [Fact]
    public void CalledTenpai_WithHonorYaku_IsNotPenalized()
    {
        var engine = NewEngine(0, Tiles("m2 m3 m4 p5 p6 p7 s1 s2 s3 z1"));
        engine.Players[1].AddMeld(MeldType.Pon, Tiles("z5 z5 z5"));

        engine.Flow();

        AssertScores(engine, Start, Start, Start, Start);
    }

    /// <summary>断幺九 2 赋 → 有役（不是「赋 &lt; 7 就不能和」），不罚。</summary>
    [Fact]
    public void CalledTenpai_WithTanyaoTwoFu_IsNotPenalized()
    {
        var engine = NewEngine(0, Tiles("m2 m3 m4 p5 p6 p7 s2 s3 s4 m7"));
        engine.Players[1].AddMeld(MeldType.Pon, Tiles("m5 m5 m5"));

        engine.Flow();

        AssertScores(engine, Start, Start, Start, Start);
    }

    /// <summary>有副露 + 听牌但一个役都数不出 → 子家罚 1400（庄 +700、另两家各 +350）。</summary>
    [Fact]
    public void CalledNoYakuTenpai_NonDealer_Pays1400()
    {
        var engine = NewEngine(0, Tiles("m2 m3 m4 p5 p6 p7 s1 s2 s3 z2"));
        engine.Players[1].AddMeld(MeldType.Pon, Tiles("m5 m5 m5"));

        engine.Flow();

        AssertScores(engine, Start + 700, Start - 1400, Start + 350, Start + 350);
    }

    /// <summary>同条件庄家 → 罚 2100（三家各 +700）。</summary>
    [Fact]
    public void CalledNoYakuTenpai_Dealer_Pays2100()
    {
        var engine = NewEngine(1, Tiles("m2 m3 m4 p5 p6 p7 s1 s2 s3 z2"));
        engine.Players[1].AddMeld(MeldType.Pon, Tiles("m5 m5 m5"));

        engine.Flow();

        AssertScores(engine, Start + 700, Start - 2100, Start + 700, Start + 700);
    }

    /// <summary>门清（无副露）+ 无役听牌 → 罚项只管鸣牌后，不罚。</summary>
    [Fact]
    public void MenzenNoYakuTenpai_IsNotPenalized()
    {
        var engine = NewEngine(0, Tiles("m2 m3 m4 p5 p6 p7 s1 s2 s3 m8 m9 z1 z2"));

        engine.Flow();

        AssertScores(engine, Start, Start, Start, Start);
    }

    /// <summary>有副露但根本没听牌 → 不罚（无役罚只针对「听牌却无役」）。</summary>
    [Fact]
    public void CalledNoten_IsNotPenalized()
    {
        var engine = NewEngine(0, Tiles("m2 m4 m7 p1 p4 p9 s2 s5 s8 z1"));
        engine.Players[1].AddMeld(MeldType.Pon, Tiles("m5 m5 m5"));

        engine.Flow();

        AssertScores(engine, Start, Start, Start, Start);
    }

    /// <summary>未听返杠仍生效：杠最多且未听者按 700/杠 向三家返还，子家额外向庄家付 700。</summary>
    [Fact]
    public void NotenWithKan_Pays700PerKan()
    {
        var engine = NewEngine(0, Tiles("m1 m3 m5 m7 m9 p1 p3 s1 s4 s7"));
        engine.Players[2].AddMeld(MeldType.Ankan, Tiles("z7 z7 z7 z7"));
        Assert.Equal(1, engine.Players[2].AnkanCount);

        engine.Flow();

        // 子家 -700×3（三家各 700）-700（给庄）= -2800；庄 +1400、另两家各 +700。
        AssertScores(engine, Start + 1400, Start + 700, Start - 2800, Start + 700);
    }

    /// <summary>流局必须翻牌。</summary>
    [Fact]
    public void Flow_RevealsHands()
    {
        var engine = NewEngine(0, Filler());

        engine.Flow();

        Assert.True(engine.RevealHands);
    }
}
