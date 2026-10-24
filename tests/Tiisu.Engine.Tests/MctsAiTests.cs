using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;

namespace Tiisu.Engine.Tests;

/// <summary>
/// C 期：修罗档 MCTS（实验性、默认关闭）。
/// 覆盖：默认关闭等价困难档、合法下标、确定性、预算回退、退化手牌、安全牌偏好、
/// rollout 步数上限、以及「不影响 Simple/Normal/Hard」。
/// <para>
/// 这些用例带时间预算，与其他测试并行跑会因为 CPU 抢占导致模拟次数不同而抖动，
/// 所以单独串行执行。
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MctsSerialCollection
{
    public const string Name = "MctsSerial";
}

[Collection(MctsSerialCollection.Name)]
public sealed class MctsAiTests
{
    // ─────────────────────────────────────────────────────────────
    // 1. 关闭时 Shura == Hard
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsDisabled_ShuraEqualsHard()
    {
        var contexts = new[]
        {
            Context(Tiles("123m456m789m11p23s5z")),
            DefenseContext(wall: 70),
            Context(Tiles("11223344556677z"), allDiscards: Rivers("7z", "", "", "")),
        };

        foreach (var ctx in contexts)
        {
            var hard = TieredAi.Decide(ctx, AiLevel.Hard, new FixedRandom(0.99));
            var shura = TieredAi.Decide(ctx, AiLevel.Shura, new FixedRandom(0.99), mctsEnabled: false);

            Assert.False(shura.Mcts);
            Assert.Equal(hard.Index, shura.Index);
            Assert.Equal(hard.NeedDeepThink, shura.NeedDeepThink);
            Assert.Equal(hard.Mistake, shura.Mistake);
            AssertSameRanking(hard, shura);
        }
    }

    [Fact]
    public void MctsDefaultOff_ShuraStillEqualsHard()
    {
        // ctx.MctsEnabled 默认 false：不传任何开关时修罗仍是困难档（C 期默认关闭的直接证据）。
        var ctx = Context(Tiles("123m456m789m11p23s5z"));

        Assert.False(ctx.MctsEnabled);

        var hard = TieredAi.Decide(ctx, AiLevel.Hard, new Mulberry32Random(20261023));
        var shura = TieredAi.Decide(ctx, AiLevel.Shura, new Mulberry32Random(20261023));

        Assert.False(shura.Mcts);
        Assert.Equal(hard.Index, shura.Index);
        AssertSameRanking(hard, shura);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 打开时返回合法下标
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsEnabled_ReturnsLegalHandIndex()
    {
        var ctx = Context(Tiles("123m456m789m11p23s5z")).WithMcts();

        var decision = TieredAi.Decide(
            ctx,
            AiLevel.Shura,
            new Mulberry32Random(7),
            mctsEnabled: true);

        Assert.True(decision.Mcts);
        Assert.InRange(decision.Index, 0, ctx.Hand.Count - 1);
        Assert.All(decision.Ranking, c => Assert.InRange(c.Index, 0, ctx.Hand.Count - 1));
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 同 seed 两次结果相同
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsEnabled_SameSeed_Reproduces()
    {
        var ctx = Context(Tiles("123m456m789m11p23s5z")).WithMcts();
        var options = new MctsOptions(32, 8, TimeSpan.FromSeconds(30));

        var first = MctsAi.ChooseDiscard(ctx, new Mulberry32Random(9981), options);
        var second = MctsAi.ChooseDiscard(ctx, new Mulberry32Random(9981), options);
        Assert.Equal(first, second);

        var decisionA = TieredAi.Decide(ctx, AiLevel.Shura, new Mulberry32Random(9981), mctsEnabled: true);
        var decisionB = TieredAi.Decide(ctx, AiLevel.Shura, new Mulberry32Random(9981), mctsEnabled: true);
        Assert.Equal(decisionA.Index, decisionB.Index);
        Assert.Equal(decisionA.NeedDeepThink, decisionB.NeedDeepThink);
        AssertSameRanking(decisionA, decisionB);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 不同 seed 允许不同，但必须合法
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsEnabled_DifferentSeeds_StayLegal()
    {
        var ctx = Context(Tiles("1m4m7m2p5p8p3s6s9s1z2z4z6z7z")).WithMcts();
        var options = new MctsOptions(16, 4, TimeSpan.FromSeconds(10));

        for (uint seed = 1; seed <= 6; seed++)
        {
            int index = MctsAi.ChooseDiscard(ctx, new Mulberry32Random(seed), options);
            Assert.InRange(index, 0, ctx.Hand.Count - 1);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 预算 = 0 / 极小 → 回退困难档，不抛异常
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsZeroBudget_FallsBackToHard()
    {
        var ctx = Context(Tiles("123m456m789m11p23s5z")).WithMcts();

        var zeroSims = MctsAi.ChooseDiscard(
            ctx,
            new Mulberry32Random(11),
            new MctsOptions(0, 8, TimeSpan.FromSeconds(1)));
        var hard = TieredAi.Decide(ctx, AiLevel.Hard, new Mulberry32Random(11));

        Assert.Equal(hard.Index, zeroSims);

        var zeroDrawsButSims = MctsAi.ChooseDiscard(
            ctx,
            new Mulberry32Random(11),
            new MctsOptions(8, 0, TimeSpan.FromSeconds(5)));
        Assert.InRange(zeroDrawsButSims, 0, ctx.Hand.Count - 1);
    }

    // ─────────────────────────────────────────────────────────────
    // 6. TimeBudget 极短 → 不崩、能返回
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsTinyTimeBudget_ReturnsWithoutThrowing()
    {
        var ctx = Context(Tiles("123m456m789m11p23s5z")).WithMcts();

        foreach (var budget in new[] { TimeSpan.Zero, TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(1) })
        {
            int index = MctsAi.ChooseDiscard(
                ctx,
                new Mulberry32Random(5),
                new MctsOptions(300, 12, budget));

            Assert.InRange(index, 0, ctx.Hand.Count - 1);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 7. 退化手牌（单张）不崩
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsSingleTileHand_DoesNotThrow()
    {
        var ctx = Context(Tiles("5z")).WithMcts();

        int index = MctsAi.ChooseDiscard(
            ctx,
            new Mulberry32Random(3),
            new MctsOptions(16, 4, TimeSpan.FromSeconds(5)));

        Assert.Equal(0, index);

        var decision = TieredAi.Decide(ctx, AiLevel.Shura, new Mulberry32Random(3), mctsEnabled: true);
        Assert.Equal(0, decision.Index);
    }

    // ─────────────────────────────────────────────────────────────
    // 8. 和牌率相近时倾向安全牌
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsEnabled_PrefersSafeDiscard_WhenWinRatesAreEqual()
    {
        // 4 组副露 + 两张单张：切 3z 听 4z、切 4z 听 3z，两种听的剩余张数都是 0
        // （3z / 4z 各 4 张全部可见）→ 和牌率、听牌率完全相同，只剩放铳危险度决定。
        // 3z 是现物（危险 0.02），4z 只被自家手牌 + 别家碰见（残局加成后 0.264）。
        var ctx = SafeDangerContext();

        var hard = TieredAi.Decide(ctx, AiLevel.Hard, new Mulberry32Random(3));
        var safe = hard.Ranking.Single(c => c.Tile == new Tile(Suit.Honor, 3));
        var risky = hard.Ranking.Single(c => c.Tile == new Tile(Suit.Honor, 4));

        Assert.Equal(0, safe.Shanten.Shanten);
        Assert.Equal(safe.Shanten.Shanten, risky.Shanten.Shanten);
        Assert.Equal(0, safe.Ukeire);
        Assert.Equal(0, risky.Ukeire);
        Assert.True(risky.Danger > safe.Danger + 0.2, $"safe={safe.Danger:F3} risky={risky.Danger:F3}");

        // 低预算（每个候选 4 次模拟）也足以在两种听牌完全同分时选安全牌。
        int index = MctsAi.ChooseDiscard(
            ctx,
            new Mulberry32Random(3),
            new MctsOptions(8, 2, TimeSpan.FromSeconds(5)));

        Assert.Equal(safe.Index, index);
    }

    // ─────────────────────────────────────────────────────────────
    // 9. MaxRolloutDraws 生效（rollout 摸牌步数上限）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Rollout_RespectsMaxDraws()
    {
        var hand = Tiles("123m456m789m11p23s"); // 13 张、2 向听，短 rollout 内不会和牌
        var threat = SimpleThreat(hand);

        foreach (int maxDraws in new[] { 0, 1, 3 })
        {
            var unseen = FullUnseen();
            var result = MctsAi.RunRollout(
                hand,
                Array.Empty<Meld>(),
                unseen,
                new Mulberry32Random(17),
                maxDraws,
                threat,
                long.MaxValue);

            Assert.InRange(result.Draws, 0, maxDraws);
            Assert.False(result.Win);
        }

        // 上限足够大时确实会摸更多张（不是永远 0）。
        var big = MctsAi.RunRollout(
            hand,
            Array.Empty<Meld>(),
            FullUnseen(),
            new Mulberry32Random(17),
            12,
            threat,
            long.MaxValue);
        Assert.InRange(big.Draws, 1, 12);
    }

    // ─────────────────────────────────────────────────────────────
    // 10. Simple / Normal / Hard 不受 MctsEnabled 影响
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MctsEnabled_DoesNotAffect_SimpleNormalHard()
    {
        var plain = Context(Tiles("123m456m789m11p23s5z"), allDiscards: Rivers("1m", "9p", "", ""));
        var withMcts = plain.WithMcts();

        foreach (var level in new[] { AiLevel.Simple, AiLevel.Normal, AiLevel.Hard })
        {
            var off = TieredAi.Decide(plain, level, new Mulberry32Random(424242));
            var on = TieredAi.Decide(withMcts, level, new Mulberry32Random(424242));

            Assert.False(on.Mcts);
            Assert.Equal(off.Index, on.Index);
            Assert.Equal(off.NeedDeepThink, on.NeedDeepThink);
            Assert.Equal(off.Mistake, on.Mistake);
            AssertSameRanking(off, on);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 11. 自适应预算口径
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AdaptiveBudget_MatchesDesign()
    {
        var deep = MctsOptions.Adaptive(needDeepThink: true);
        var normal = MctsOptions.Adaptive(needDeepThink: false);

        Assert.Equal(300, deep.MaxSimulations);
        Assert.Equal(100, normal.MaxSimulations);
        Assert.Equal(AiTuning.Default.MctsMaxRolloutDraws, deep.MaxRolloutDraws);
        Assert.Equal(TimeSpan.FromMilliseconds(200), deep.TimeBudget);
        Assert.Equal(TimeSpan.FromMilliseconds(200), normal.TimeBudget);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private static void AssertSameRanking(AiDecision expected, AiDecision actual)
    {
        Assert.Equal(expected.Ranking.Count, actual.Ranking.Count);
        for (int i = 0; i < expected.Ranking.Count; i++)
        {
            Assert.Equal(expected.Ranking[i].Index, actual.Ranking[i].Index);
            Assert.Equal(expected.Ranking[i].Score, actual.Ranking[i].Score, 9);
            Assert.Equal(expected.Ranking[i].Danger, actual.Ranking[i].Danger, 9);
        }
    }

    private static int[] FullUnseen()
    {
        var unseen = new int[34];
        for (int i = 0; i < unseen.Length; i++)
        {
            unseen[i] = 4;
        }

        return unseen;
    }

    private static ThreatInput SimpleThreat(IReadOnlyList<Tile> hand) =>
        new(
            NoRivers(),
            new[] { Array.Empty<Meld>(), Array.Empty<Meld>(), Array.Empty<Meld>(), Array.Empty<Meld>() },
            new[] { 70, 70, 70, 70 },
            70,
            new[] { 25000, 25000, 25000, 25000 },
            0,
            0)
        {
            SelfHand = hand,
        };

    /// <summary>现物 3z（0.02） vs 只被别家碰见 + 残局的 4z（0.264），两种听都已是死听。</summary>
    private static AiDecisionContext SafeDangerContext()
    {
        var melds = new List<Meld>
        {
            new(MeldType.Chi, Tiles("123m")),
            new(MeldType.Chi, Tiles("456m")),
            new(MeldType.Chi, Tiles("789m")),
            new(MeldType.Pon, Tiles("111p")),
        };

        return new AiDecisionContext(
            Tiles("3z4z"),
            melds,
            Array.Empty<Tile>(),
            Rivers("3z3z3z", "", "", ""),
            new List<Meld> { new(MeldType.Pon, Tiles("444z")) },
            0,
            8,
            new[] { 25000, 25000, 25000, 25000 },
            SeatWind.East,
            SeatWind.East,
            0,
            0);
    }

    private static AiDecisionContext Context(
        List<Tile> hand,
        IReadOnlyList<IReadOnlyList<Tile>>? allDiscards = null,
        int wall = 70,
        IReadOnlyList<Meld>? opponentMelds = null) =>
        new(
            hand,
            Array.Empty<Meld>(),
            Array.Empty<Tile>(),
            allDiscards ?? NoRivers(),
            opponentMelds ?? Array.Empty<Meld>(),
            0,
            wall,
            new[] { 25000, 25000, 25000, 25000 },
            SeatWind.East,
            SeatWind.East,
            0,
            0);

    private static AiDecisionContext DefenseContext(int wall) =>
        new(
            Tiles("123m456m789m111p2z5s"),
            Array.Empty<Meld>(),
            Array.Empty<Tile>(),
            Rivers("2z", "", "", ""),
            new List<Meld>
            {
                new(MeldType.Chi, Tiles("345s")),
                new(MeldType.Chi, Tiles("567s")),
            },
            0,
            wall,
            new[] { 25000, 25000, 25000, 25000 },
            SeatWind.East,
            SeatWind.East,
            0,
            0);

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

    private sealed class FixedRandom : IRandomSource
    {
        private readonly double _value;

        public FixedRandom(double value) => _value = value;

        public double NextDouble() => _value;
    }
}

/// <summary>测试用：把同一个决策上下文打开 MCTS（record 的 init 属性）。</summary>
internal static class MctsTestContextExtensions
{
    public static Tiisu.Engine.Ai.AiDecisionContext WithMcts(
        this Tiisu.Engine.Ai.AiDecisionContext ctx) =>
        ctx with { MctsEnabled = true };
}
