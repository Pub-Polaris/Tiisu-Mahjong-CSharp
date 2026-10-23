using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Ai;
using EngineMeld = Tiisu.Engine.Meld;

namespace Tiisu.Engine.Tests;

/// <summary>
/// 报听（叫胡）AI 决策：Simple 不报 / Normal 听牌即报 / Hard 看期望赋与放铳风险 /
/// Shura 更严；非听牌一律不报；报听弃牌不破坏听牌形。
/// 规则口径见 <c>docs/DSH/报听机制_261023.md</c>。
/// </summary>
public sealed class AiCallPolicyTests
{
    // 14 张形，打出 9p 后单钓 5z；期望赋 22（混一色 + 缺门）。
    private const string HighValueHand = "1111m23m456m789m5z9p";

    // 打出 9p 后听 6m / 9m：6m 12 赋、9m 无役（0 赋计入分母）→ 期望赋 6，低于 Hard 门槛。
    private const string LowValueHand = "78m234567p55678s9p";

    // 打出 9m 后单钓 1z，期望赋 9：过 Hard 的 7，不到 Shura 的 10。
    private const string MidValueHand = "678m444999p456s1z9m";

    // 任意弃牌都不听牌。
    private const string NotenHand = "11389m22445p2469s";

    [Fact]
    public void Simple_NeverDeclares_EvenWhenTenpai()
    {
        var ctx = Context(Tiles(HighValueHand));

        var decision = AiCallPolicy.Evaluate(ctx, AiLevel.Simple);

        Assert.False(decision.Declare);
        Assert.Equal(-1, decision.DiscardIndex);
    }

    [Fact]
    public void Normal_DeclaresAsSoonAsTenpai()
    {
        var ctx = Context(Tiles(HighValueHand));

        var decision = AiCallPolicy.Evaluate(ctx, AiLevel.Normal, preferenceOrder: Order(ctx, new Tile(Suit.Pin, 9)));

        Assert.True(decision.Declare);
        Assert.Equal(new Tile(Suit.Pin, 9), ctx.Hand[decision.DiscardIndex]);
        Assert.True(decision.WaitCount > 0);
        Assert.NotEqual(0UL, decision.WaitMask);
    }

    [Fact]
    public void Normal_DeclaresEvenWhenExpectedFuIsLow()
    {
        var ctx = Context(Tiles(LowValueHand));

        var decision = AiCallPolicy.Evaluate(ctx, AiLevel.Normal, preferenceOrder: Order(ctx, new Tile(Suit.Pin, 9)));

        Assert.True(decision.Declare);
        Assert.True(decision.ExpectedFu < 7.0);
    }

    [Fact]
    public void Hard_DeclaresOnHighExpectedFu()
    {
        var ctx = Context(Tiles(HighValueHand));

        var decision = AiCallPolicy.Evaluate(ctx, AiLevel.Hard, preferenceOrder: Order(ctx, new Tile(Suit.Pin, 9)));

        Assert.True(decision.Declare);
        Assert.True(decision.ExpectedFu >= AiTuning.Default.HardCallMinExpectedFu);
        Assert.True(decision.Danger <= AiTuning.Default.HardCallMaxDanger);
    }

    [Fact]
    public void Hard_SkipsLowExpectedFu()
    {
        var ctx = Context(Tiles(LowValueHand));
        var preferred = Order(ctx, new Tile(Suit.Pin, 9));

        var normal = AiCallPolicy.Evaluate(ctx, AiLevel.Normal, preferenceOrder: preferred);
        var hard = AiCallPolicy.Evaluate(ctx, AiLevel.Hard, preferenceOrder: preferred);

        Assert.True(normal.Declare);
        Assert.False(hard.Declare);
        Assert.True(normal.ExpectedFu < AiTuning.Default.HardCallMinExpectedFu);
    }

    [Fact]
    public void Shura_IsStricterThanHard()
    {
        // 期望赋 9：Hard 的 7 过线，Shura 的 10 不过。
        var ctx = Context(Tiles(MidValueHand));
        var preferred = Order(ctx, new Tile(Suit.Man, 9));

        var hard = AiCallPolicy.Evaluate(ctx, AiLevel.Hard, preferenceOrder: preferred);
        var shura = AiCallPolicy.Evaluate(ctx, AiLevel.Shura, preferenceOrder: preferred);

        Assert.True(hard.Declare);
        Assert.False(shura.Declare);
        Assert.InRange(hard.ExpectedFu, 7.0, 10.0 - 1e-9);
    }

    [Fact]
    public void Shura_DeclaresOnVeryHighExpectedFu()
    {
        var ctx = Context(Tiles(HighValueHand));

        var decision = AiCallPolicy.Evaluate(ctx, AiLevel.Shura, preferenceOrder: Order(ctx, new Tile(Suit.Pin, 9)));

        Assert.True(decision.Declare);
        Assert.True(decision.ExpectedFu >= AiTuning.Default.ShuraCallMinExpectedFu);
    }

    [Fact]
    public void Shura_BlocksCall_WhenDiscardIsTooDangerous()
    {
        // 2z 是现物；5s 对两组索子吃（345s / 567s）很危险。只允许打 5s 报听时，
        // Hard 的 0.60 门槛放行，Shura 的 0.35 门槛拒绝。
        var ctx = Context(
            Tiles("123m456m789m111p2z5s"),
            rivers: Rivers("2z", "", "", ""),
            opponentMelds: new List<EngineMeld>
            {
                new(MeldType.Chi, Tiles("345s")),
                new(MeldType.Chi, Tiles("567s")),
            });
        var only = Order(ctx, new Tile(Suit.Sou, 5));

        var hard = AiCallPolicy.Evaluate(ctx, AiLevel.Hard, preferenceOrder: only);
        var shura = AiCallPolicy.Evaluate(ctx, AiLevel.Shura, preferenceOrder: only);

        Assert.True(hard.Declare);
        Assert.False(shura.Declare);
        Assert.True(hard.Danger > AiTuning.Default.ShuraCallMaxDanger);
        Assert.True(hard.Danger <= AiTuning.Default.HardCallMaxDanger);
    }

    [Fact]
    public void NoLevel_Declares_WhenNotTenpai()
    {
        var ctx = Context(Tiles(NotenHand));

        foreach (var level in Enum.GetValues<AiLevel>())
        {
            Assert.False(AiCallPolicy.ShouldDeclareCall(ctx, level), level.ToString());
        }
    }

    [Fact]
    public void DeclaredDiscard_PreservesWaitMask()
    {
        var ctx = Context(Tiles(HighValueHand));
        var decision = AiCallPolicy.Evaluate(ctx, AiLevel.Hard, preferenceOrder: Order(ctx, new Tile(Suit.Pin, 9)));
        Assert.True(decision.Declare);

        var hand = new List<Tile>(ctx.Hand);
        hand.RemoveAt(decision.DiscardIndex);

        var engine = new MahjongEngine();
        ulong mask = engine.WaitMaskFor(
            hand,
            ctx.Melds,
            ctx.AnkanCount,
            ctx.MinkanCount,
            ctx.SeatWind,
            firstTurn: false);

        Assert.Equal(decision.WaitMask, mask);
        Assert.NotEqual(0UL, mask);
    }

    [Fact]
    public void AlreadyThirteenTiles_CanDeclareWithoutDiscard()
    {
        var ctx = Context(Tiles("1111m23m456m789m5z"));

        var decision = AiCallPolicy.Evaluate(ctx, AiLevel.Hard);

        Assert.True(decision.Declare);
        Assert.Equal(-1, decision.DiscardIndex);
        Assert.Equal(1UL << 31, decision.WaitMask); // 单钓白（5z）
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<int> Order(AiDecisionContext ctx, Tile tile)
    {
        int index = ctx.Hand.ToList().IndexOf(tile);
        Assert.True(index >= 0, "手牌里没有 " + tile);
        return new[] { index };
    }

    private static AiDecisionContext Context(
        List<Tile> hand,
        IReadOnlyList<IReadOnlyList<Tile>>? rivers = null,
        IReadOnlyList<EngineMeld>? opponentMelds = null) =>
        new(
            hand,
            Array.Empty<EngineMeld>(),
            Array.Empty<Tile>(),
            rivers ?? NoRivers(),
            opponentMelds ?? Array.Empty<EngineMeld>(),
            0,
            70,
            new[] { 25000, 25000, 25000, 25000 },
            SeatWind.East,
            SeatWind.East,
            0,
            0)
        {
            PlayerIndex = 0,
        };

    private static IReadOnlyList<IReadOnlyList<Tile>> NoRivers() =>
        new[] { new List<Tile>(), new List<Tile>(), new List<Tile>(), new List<Tile>() };

    private static IReadOnlyList<IReadOnlyList<Tile>> Rivers(params string[] specs) =>
        specs.Select(spec => (IReadOnlyList<Tile>)Tiles(spec)).ToList();

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
