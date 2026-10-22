using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;

namespace Tiisu.Engine.Tests;

/// <summary>
/// A 期新 AI：进张 / 分档弃牌 / 难度分配 / 失误模型 / 自适应思考标记。
/// </summary>
public sealed class TieredAiTests
{
    // ─────────────────────────────────────────────────────────────
    // 进张
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Ukeire_TankiWait_CountsThreeRemaining()
    {
        var hand = Tiles("123m456m789m111p2z");

        var result = Ukeire.Calculate(hand, Array.Empty<Meld>(), NoRivers());

        var entry = Assert.Single(result.Entries);
        Assert.Equal(new Tile(Suit.Honor, 2), entry.Tile);
        Assert.Equal(3, entry.Remaining);
        Assert.Equal(3, result.TotalRemaining);
        Assert.Equal(0, result.Current.Shanten);
    }

    [Fact]
    public void Ukeire_RyanmenWait_SubtractsVisibleRivers()
    {
        var hand = Tiles("123m456m789m11p23s");
        var rivers = Rivers("1s1s", "4s", "", "");

        var result = Ukeire.Calculate(hand, Array.Empty<Meld>(), rivers);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(5, result.TotalRemaining);
        Assert.Equal(5, result.Entries.Sum(e => e.Remaining));

        var oneS = Assert.Single(result.Entries, e => e.Tile == new Tile(Suit.Sou, 1));
        var fourS = Assert.Single(result.Entries, e => e.Tile == new Tile(Suit.Sou, 4));
        Assert.Equal(2, oneS.Remaining);
        Assert.Equal(3, fourS.Remaining);
    }

    [Fact]
    public void Ukeire_OpenHand_SubtractsOwnHandAndRiver()
    {
        var hand = Tiles("123m456m789m1p");
        var melds = new List<Meld> { new(MeldType.Pon, Tiles("111z")) };
        var rivers = Rivers("1p1p", "", "", "");

        var result = Ukeire.Calculate(hand, melds, rivers);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(new Tile(Suit.Pin, 1), entry.Tile);
        Assert.Equal(1, entry.Remaining);
        Assert.Equal(1, result.TotalRemaining);
    }

    [Fact]
    public void Ukeire_AllFourCopiesVisible_ReturnsNoEntries()
    {
        var hand = Tiles("123m456m789m1p");
        var melds = new List<Meld> { new(MeldType.Pon, Tiles("111z")) };
        var rivers = Rivers("1p1p1p", "", "", "");

        var result = Ukeire.Calculate(hand, melds, rivers);

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.TotalRemaining);
    }

    // ─────────────────────────────────────────────────────────────
    // 分档弃牌
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AllLevels_AreDeterministic_AndReturnLegalIndex()
    {
        var ctx = Context(Tiles("123m456m789m11p23s5z"));

        foreach (var level in Enum.GetValues<AiLevel>())
        {
            var first = TieredAi.Decide(ctx, level, new Mulberry32Random(20261023));
            var second = TieredAi.Decide(ctx, level, new Mulberry32Random(20261023));

            Assert.InRange(first.Index, 0, ctx.Hand.Count - 1);
            Assert.Equal(first.Index, second.Index);
            Assert.Equal(first.NeedDeepThink, second.NeedDeepThink);
            Assert.Equal(first.Mistake, second.Mistake);
            Assert.Equal(first.Ranking.Count, second.Ranking.Count);
            for (int i = 0; i < first.Ranking.Count; i++)
            {
                Assert.Equal(first.Ranking[i].Index, second.Ranking[i].Index);
                Assert.Equal(first.Ranking[i].Score, second.Ranking[i].Score, 9);
            }
        }
    }

    [Fact]
    public void Simple_MatchesLegacyHeuristic_AndIgnoresUkeire()
    {
        var ctx = Context(Tiles("123m456m789m11p23s5z"));
        var decision = TieredAi.Decide(ctx, AiLevel.Simple, new FixedRandom(0.99));

        int legacy = AiPlayer.ChooseDiscardIndex(ctx.Hand, 0.0);

        Assert.Equal(legacy, decision.Index);
        Assert.All(decision.Ranking, candidate => Assert.Equal(0, candidate.Ukeire));
    }

    [Fact]
    public void Normal_ChoosesMaximumUkeire()
    {
        var ctx = Context(Tiles("123m456m789m11p23s5z"));

        var decision = TieredAi.Decide(ctx, AiLevel.Normal, new FixedRandom(0.99));

        Assert.False(decision.Mistake);
        Assert.Equal(decision.Ranking[0].Index, decision.Index);
        Assert.Equal(decision.Ranking.Max(c => c.Ukeire), decision.Ranking[0].Ukeire);
    }

    [Fact]
    public void Hard_PrefersGenbutsu_WhenUkeireIsEqual()
    {
        // 七对子形状里每张字牌都是「切掉后单钓同一张」，进张 / 赋完全相同；7z 是现物。
        var ctx = Context(
            Tiles("11223344556677z"),
            allDiscards: Rivers("7z", "", "", ""));

        var normal = TieredAi.Decide(ctx, AiLevel.Normal, new FixedRandom(0.99));
        var hard = TieredAi.Decide(ctx, AiLevel.Hard, new FixedRandom(0.99));

        var hardChoice = hard.Ranking.Single(c => c.Index == hard.Index);
        var normalChoice = normal.Ranking.Single(c => c.Index == normal.Index);
        Assert.Equal(new Tile(Suit.Honor, 7), hardChoice.Tile);
        Assert.NotEqual(new Tile(Suit.Honor, 7), normalChoice.Tile);

        var best = hard.Ranking[0];
        double expected = best.Ukeire * 10.0
            + best.ExpectedValue
            + (best.Genbutsu ? 12.0 : 0.0)
            - best.KeepScore * 0.01
            - best.Danger * 30.0;
        Assert.Equal(expected, best.Score, 9);
    }

    // ─────────────────────────────────────────────────────────────
    // B 期：防守威胁接入分档
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Hard_PrefersSafeTile_OverOneExtraUkeire()
    {
        // 4 组副露 + 两张单张：切 2z（现物）听 5s，还剩 1 张；切 5s（危险）听 2z，还剩 2 张。
        // 只有这两个候选，进张差 1，危险差足够大。
        var hand = Tiles("2z5s");
        var ownMelds = new List<Meld>
        {
            new(MeldType.Chi, Tiles("123m")),
            new(MeldType.Chi, Tiles("456m")),
            new(MeldType.Chi, Tiles("789m")),
            new(MeldType.Pon, Tiles("111p")),
        };
        var opponentMelds = new List<Meld>
        {
            new(MeldType.Chi, Tiles("345s")),
            new(MeldType.Chi, Tiles("567s")),
        };

        var ctx = new AiDecisionContext(
            hand,
            ownMelds,
            Array.Empty<Tile>(),
            Rivers("2z", "", "", ""),
            opponentMelds,
            0,
            70,
            new[] { 25000, 25000, 25000, 25000 },
            SeatWind.East,
            SeatWind.East,
            0,
            0);

        var decision = TieredAi.Decide(ctx, AiLevel.Hard, new FixedRandom(0.99));
        var safe = decision.Ranking.Single(c => c.Tile == Tiles("2z")[0]);
        var risky = decision.Ranking.Single(c => c.Tile == Tiles("5s")[0]);

        Assert.Equal(2, decision.Ranking.Count);
        Assert.Equal(risky.Ukeire - 1, safe.Ukeire);
        Assert.True(safe.Danger < risky.Danger - 0.3);
        Assert.False(decision.Mistake);
        Assert.Equal(safe.Index, decision.Index);
    }

    [Fact]
    public void Shura_UsesSameDefenseAsHard()
    {
        var ctx = DefenseContext(wall: 70);
        var hard = TieredAi.Decide(ctx, AiLevel.Hard, new FixedRandom(0.99));
        var shura = TieredAi.Decide(ctx, AiLevel.Shura, new FixedRandom(0.99));

        Assert.Equal(hard.Index, shura.Index);
        Assert.Equal(hard.Ranking[0].Score, shura.Ranking[0].Score, 9);
        Assert.Equal(hard.Ranking[0].Danger, shura.Ranking[0].Danger, 9);
    }

    [Fact]
    public void Normal_DefendsOnlyInLateGame_WhenShantenIsFar()
    {
        var hand = Tiles("1m4m7m2p5p8p3s6s9s1z2z4z6z7z");
        var rivers = Rivers("1m", "9p", "", "");

        var earlyCtx = Context(hand, allDiscards: rivers, wall: 70);
        var lateCtx = Context(hand, allDiscards: rivers, wall: 10);

        Assert.True(Shanten.Calculate(hand, Array.Empty<Meld>()).Shanten >= 2);

        var early = TieredAi.Decide(earlyCtx, AiLevel.Normal, new FixedRandom(0.99));
        var late = TieredAi.Decide(lateCtx, AiLevel.Normal, new FixedRandom(0.99));

        var earlyFour = early.Ranking.Single(c => c.Tile == Tiles("4m")[0]);
        var lateFour = late.Ranking.Single(c => c.Tile == Tiles("4m")[0]);

        Assert.Equal(0.0, earlyFour.Danger, 9);
        Assert.True(lateFour.Danger > 0.2);

        // 开局：只有进张 + 旧启发式平手；残局：再减去 5 × 危险度。
        double lateExpected = earlyFour.Score - lateFour.Danger * 5.0;
        Assert.Equal(lateExpected, lateFour.Score, 9);
    }

    [Fact]
    public void Normal_DoesNotDefend_WhenCloseToTenpai()
    {
        var hand = Tiles("123m456m789m11p23s5z");
        var ctx = Context(hand, allDiscards: Rivers("1m", "9p", "", ""), wall: 10);

        var decision = TieredAi.Decide(ctx, AiLevel.Normal, new FixedRandom(0.99));

        Assert.All(decision.Ranking, candidate => Assert.Equal(0.0, candidate.Danger, 9));
    }

    [Fact]
    public void Simple_IgnoresDanger()
    {
        var hand = Tiles("1m4m7m2p5p8p3s6s9s1z2z4z6z7z");
        var rivers = Rivers("1m", "9p", "", "");

        var early = TieredAi.Decide(
            Context(hand, allDiscards: rivers, wall: 70),
            AiLevel.Simple,
            new FixedRandom(0.99));
        var late = TieredAi.Decide(
            Context(hand, allDiscards: rivers, wall: 10),
            AiLevel.Simple,
            new FixedRandom(0.99));

        Assert.Equal(early.Index, late.Index);
        Assert.All(early.Ranking, candidate => Assert.Equal(0.0, candidate.Danger, 9));
        Assert.All(late.Ranking, candidate => Assert.Equal(0.0, candidate.Danger, 9));
        for (int i = 0; i < early.Ranking.Count; i++)
        {
            Assert.Equal(early.Ranking[i].Score, late.Ranking[i].Score, 9);
        }
    }

    [Fact]
    public void Tuning_DefenseWeights_MatchDesign()
    {
        Assert.Equal(30.0, AiTuning.Default.HardDangerWeight, 9);
        Assert.Equal(5.0, AiTuning.Default.NormalDangerWeight, 9);
        Assert.Equal(30, AiTuning.Default.NormalDangerWallThreshold);
        Assert.Equal(2, AiTuning.Default.NormalDangerShantenThreshold);
    }

    [Fact]
    public void NeedDeepThink_TrueNearTenpai()
    {
        // 最佳打牌进入 1 向听以内（进听 / 听牌关键点）。
        var ctx = Context(Tiles("123m456m789m111p2z3z"));

        var decision = TieredAi.Decide(ctx, AiLevel.Normal, new FixedRandom(0.99));

        Assert.InRange(decision.Ranking[0].Shanten.Shanten, -1, 1);
        Assert.True(decision.NeedDeepThink);
    }

    [Fact]
    public void Mistake_SwapsToSecondCandidate()
    {
        var ctx = Context(Tiles("123m456m789m11p23s5z"));

        var decision = TieredAi.Decide(ctx, AiLevel.Normal, new SequenceRandom(0.0));

        Assert.True(decision.Mistake);
        Assert.Equal(decision.Ranking[1].Index, decision.Index);
    }

    // ─────────────────────────────────────────────────────────────
    // 难度分配
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Lineup_DefaultWeights_ApproximateThirtyFortyFiveTwentyFive()
    {
        var counts = new int[4];
        int total = 0;

        for (uint seed = 0; seed < 2000; seed++)
        {
            var lineup = AiLineup.FromWeights(AiLineup.DefaultWeights, seed);
            foreach (var level in lineup.Levels)
            {
                counts[(int)level]++;
                total++;
            }
        }

        double[] expected = { 0.30, 0.45, 0.20, 0.05 };
        for (int i = 0; i < 4; i++)
        {
            double actual = counts[i] / (double)total;
            Assert.True(
                Math.Abs(actual - expected[i]) < 0.035,
                $"档位 {(AiLevel)i} 比例 {actual:P2}，期望 {expected[i]:P2}");
        }
    }

    [Fact]
    public void Lineup_RespectsSeatLocks_AndIgnoresHumanSeat()
    {
        var lineup = AiLineup.FromWeights(
            AiLineup.DefaultWeights,
            seed: 123,
            lockSeat: new[] { 0, 3, -1, 1 },
            humanSeats: new[] { false, false, true, false });

        Assert.Equal(AiLevel.Simple, lineup[0]);
        Assert.Equal(AiLevel.Shura, lineup[1]);
        Assert.True(lineup.HumanSeats[2]);
        Assert.Equal(AiLevel.Normal, lineup[3]);
    }

    [Fact]
    public void Lineup_InvalidWeights_FallBackToDefaults()
    {
        var lineup = AiLineup.FromWeights(new[] { 0, 0, 0, 0 }, 7);

        Assert.Equal(AiLineup.DefaultWeights, lineup.Weights);
    }

    // ─────────────────────────────────────────────────────────────
    // 失误 / 紧张度
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Mistake_BaselineRates_MatchDesign()
    {
        var calm = Context(Tiles("123m456m789m11p23s5z"), wall: 70);

        Assert.Equal(0.20, MistakeModel.BaselineFailureRate(AiLevel.Simple), 9);
        Assert.Equal(0.08, MistakeModel.BaselineFailureRate(AiLevel.Normal), 9);
        Assert.Equal(0.02, MistakeModel.BaselineFailureRate(AiLevel.Hard), 9);
        Assert.Equal(0.00, MistakeModel.BaselineFailureRate(AiLevel.Shura), 9);

        Assert.Equal(0.20, MistakeModel.FailureRate(calm, AiLevel.Simple), 9);
        Assert.Equal(0.08, MistakeModel.FailureRate(calm, AiLevel.Normal), 9);
        Assert.Equal(0.02, MistakeModel.FailureRate(calm, AiLevel.Hard), 9);
        Assert.Equal(0.00, MistakeModel.FailureRate(calm, AiLevel.Shura), 9);
    }

    [Fact]
    public void Mistake_HighTension_AddsOneToThreePoints_AndShuraStaysZero()
    {
        var tense = Context(
            Tiles("123m456m789m11p23s5z"),
            wall: 10,
            scores: new[] { 5000, 40000, 30000, 20000 },
            opponentMelds: NineOpponentMelds());

        double hardCalm = MistakeModel.FailureRate(
            Context(Tiles("123m456m789m11p23s5z"), wall: 70), AiLevel.Hard);
        double hardTense = MistakeModel.FailureRate(tense, AiLevel.Hard);

        Assert.InRange(hardTense - hardCalm, 0.01 - 1e-9, 0.03 + 1e-9);
        Assert.True(MistakeModel.FailureRate(tense, AiLevel.Simple) > 0.20);
        Assert.Equal(0.0, MistakeModel.FailureRate(tense, AiLevel.Shura), 9);
    }

    [Fact]
    public void Mistake_SameRngSequence_Reproduces()
    {
        var calm = Context(Tiles("123m456m789m11p23s5z"), wall: 70);

        bool first = MistakeModel.ShouldMistake(calm, AiLevel.Simple, new SequenceRandom(0.19));
        bool second = MistakeModel.ShouldMistake(calm, AiLevel.Simple, new SequenceRandom(0.19));
        bool third = MistakeModel.ShouldMistake(calm, AiLevel.Simple, new SequenceRandom(0.21));

        Assert.True(first);
        Assert.True(second);
        Assert.False(third);

        var shuraRandom = new SequenceRandom(0.0);
        Assert.False(MistakeModel.ShouldMistake(calm, AiLevel.Shura, shuraRandom));
        Assert.Equal(0, shuraRandom.Consumed);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private static AiDecisionContext Context(
        List<Tile> hand,
        IReadOnlyList<IReadOnlyList<Tile>>? allDiscards = null,
        int wall = 70,
        int[]? scores = null,
        IReadOnlyList<Meld>? opponentMelds = null,
        SeatWind seat = SeatWind.East) =>
        new(
            hand,
            Array.Empty<Meld>(),
            Array.Empty<Tile>(),
            allDiscards ?? NoRivers(),
            opponentMelds ?? Array.Empty<Meld>(),
            0,
            wall,
            scores ?? new[] { 25000, 25000, 25000, 25000 },
            seat,
            SeatWind.East,
            0,
            0);

    private static IReadOnlyList<IReadOnlyList<Tile>> NoRivers() =>
        new[] { new List<Tile>(), new List<Tile>(), new List<Tile>(), new List<Tile>() };

    private static IReadOnlyList<IReadOnlyList<Tile>> Rivers(params string[] specs) =>
        specs.Select(spec => (IReadOnlyList<Tile>)Tiles(spec)).ToList();

    private static List<Meld> NineOpponentMelds()
    {
        var melds = new List<Meld>();
        for (int i = 0; i < 9; i++)
        {
            melds.Add(new Meld(MeldType.Pon, Tiles("111z")));
        }

        return melds;
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

    /// <summary>B 期防守用例的固定牌局：现物 2z + 危险的 5s（对手两组吃都含 5s）。</summary>
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

    private sealed class FixedRandom : IRandomSource
    {
        private readonly double _value;

        public FixedRandom(double value) => _value = value;

        public double NextDouble() => _value;
    }

    private sealed class SequenceRandom : IRandomSource
    {
        private readonly Queue<double> _values = new();

        public SequenceRandom(params double[] values)
        {
            foreach (double value in values)
            {
                _values.Enqueue(value);
            }
        }

        public int Consumed { get; private set; }

        public double NextDouble()
        {
            Consumed++;
            return _values.Count > 0 ? _values.Dequeue() : 0.5;
        }
    }
}
