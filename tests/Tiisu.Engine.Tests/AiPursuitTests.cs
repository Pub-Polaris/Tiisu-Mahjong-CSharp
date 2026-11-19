using Tiisu.Core;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;

namespace Tiisu.Engine.Tests;

/// <summary>A 期 7Neuu 自动追役原型：目标识别、门清/副露规则、进入/退出、温度采样。</summary>
public sealed class AiPursuitTests
{
    // ─────────────────────────────────────────────────────────────
    // 进入 / 退出
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void 六对子_早中期_进入七对追役()
    {
        var tracker = new AiPursuit();
        var ctx = Context(Tiles("1m1m2m2m3m3m4m4m5p5p6p6p7s8s"), wall: 60);

        var state = tracker.Observe(ctx, AiStyle.Default);

        Assert.True(state.Active);
        Assert.Equal(PursuitTarget.SevenPairs, state.Target);
        Assert.InRange(state.Progress, 0.84, 0.86);
        Assert.Equal(1, tracker.Entries);
        Assert.Equal(0, tracker.Exits);
    }

    [Fact]
    public void 牌山不足_不进入追役()
    {
        var tracker = new AiPursuit();
        var ctx = Context(Tiles("1m1m2m2m3m3m4m4m5p5p6p6p7s8s"), wall: 10);

        var state = tracker.Observe(ctx, AiStyle.Default);

        Assert.False(state.Active);
        Assert.Equal(PursuitTarget.None, state.Target);
        Assert.Equal(0, tracker.Entries);
    }

    [Fact]
    public void 已和牌_不进入追役()
    {
        var tracker = new AiPursuit();
        // 标准形已经和牌（向听 -1），没有「追役」的意义。
        var ctx = Context(Tiles("123m456m789m111p22s"), wall: 60);

        var state = tracker.Observe(ctx, AiStyle.Default);

        Assert.False(state.Active);
    }

    [Fact]
    public void 大比分落后_关键局_不进入追役()
    {
        var tracker = new AiPursuit();
        var ctx = Context(
            Tiles("1m1m2m2m3m3m4m4m5p5p6p6p7s8s"),
            wall: 60,
            scores: new[] { 8000, 40000, 30000, 22000 });

        var state = tracker.Observe(ctx, AiStyle.Default);

        Assert.False(state.Active);
        Assert.True(AiPursuit.IsCritical(ctx));
    }

    [Fact]
    public void 目标进度恶化_自动退出()
    {
        var tracker = new AiPursuit();
        var entered = tracker.Observe(
            Context(Tiles("1m1m2m2m3m3m4m4m5p5p6p6p7s8s"), wall: 60), AiStyle.Default);
        Assert.True(entered.Active);

        var degraded = tracker.Observe(
            Context(Tiles("1m1m2m2m3m3m4m5m6m7p8p9s9s"), wall: 56), AiStyle.Default);

        Assert.False(degraded.Active);
        Assert.Equal(1, tracker.Exits);
    }

    [Fact]
    public void 牌山进入终盘_自动退出()
    {
        var tracker = new AiPursuit();
        var hand = Tiles("1m1m2m2m3m3m4m4m5p5p6p6p7s8s");
        Assert.True(tracker.Observe(Context(hand, wall: 60), AiStyle.Default).Active);

        var late = tracker.Observe(Context(hand, wall: 15), AiStyle.Default);

        Assert.False(late.Active);
        Assert.Equal(1, tracker.Exits);
    }

    [Fact]
    public void 追役权重为0_永不进入()
    {
        var tracker = new AiPursuit();
        var style = AiStyle.Default with { Pursuit = 0.0 };

        var state = tracker.Observe(Context(Tiles("1m1m2m2m3m3m4m4m5p5p6p6p7s8s"), wall: 60), style);

        Assert.False(state.Active);
    }

    // ─────────────────────────────────────────────────────────────
    // 目标识别
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void 字一色形态_允许副露()
    {
        var tracker = new AiPursuit();
        var ctx = Context(Tiles("1z1z1z2z2z2z3z3z3z4z4z5z5z6z"), wall: 60);

        var state = tracker.Observe(ctx, AiStyle.Default);

        Assert.True(state.Active);
        Assert.Equal(PursuitTarget.AllHonors, state.Target);
        Assert.False(state.RequiresClosed);
        Assert.True(AiPursuit.AllowsMeld(state, AiStyle.Default, Tiles("6z6z6z")));
    }

    [Fact]
    public void 大七星形态_优先进大七星且必须门清()
    {
        var tracker = new AiPursuit();
        var ctx = Context(Tiles("1z1z2z2z3z3z4z4z5z5z6z6z7z1m"), wall: 60);

        var state = tracker.Observe(ctx, AiStyle.Default);

        Assert.True(state.Active);
        Assert.Equal(PursuitTarget.BigSevenStars, state.Target);
        Assert.True(state.RequiresClosed);
        Assert.False(AiPursuit.AllowsMeld(state, AiStyle.Default, Tiles("1z1z1z")));
    }

    [Fact]
    public void 九莲形态_识别为目标_门清禁止副露()
    {
        var tracker = new AiPursuit();
        var ctx = Context(Tiles("1112345678999m5p"), wall: 60);

        var state = tracker.Observe(ctx, AiStyle.Default);

        Assert.True(state.Active);
        Assert.Equal(PursuitTarget.NineGates, state.Target);
        Assert.True(state.RequiresClosed);
        Assert.False(AiPursuit.AllowsMeld(state, AiStyle.Default, Tiles("234m")));
    }

    [Fact]
    public void 门清目标静态规则_七对与大七星与四暗杠都要求门清()
    {
        Assert.True(AiPursuit.RequiresClosed(PursuitTarget.SevenPairs));
        Assert.True(AiPursuit.RequiresClosed(PursuitTarget.BigSevenStars));
        Assert.True(AiPursuit.RequiresClosed(PursuitTarget.NineGates));
        Assert.True(AiPursuit.RequiresClosed(PursuitTarget.FourAnkan));
        Assert.False(AiPursuit.RequiresClosed(PursuitTarget.AllHonors));
        Assert.False(AiPursuit.RequiresClosed(PursuitTarget.Flush));
    }

    [Fact]
    public void 追役权重高_减少混一色副露()
    {
        var state = new PursuitState(PursuitTarget.Flush, true, 1.0, 1.0, 1.0, (int)Suit.Man);

        Assert.True(AiPursuit.AllowsMeld(state, AiStyle.Default, Tiles("123m")));
        // 目标花色以外的副露直接拒绝。
        Assert.False(AiPursuit.AllowsMeld(state, AiStyle.Default, Tiles("123p")));
        // 副露权重低 / 追役权重高且目标接近成型 → 不副露。
        Assert.False(AiPursuit.AllowsMeld(state, AiStyle.Default with { Meld = 0.4 }, Tiles("123m")));
        Assert.False(AiPursuit.AllowsMeld(state, AiStyle.Default with { Pursuit = 2.0 }, Tiles("123m")));
    }

    [Fact]
    public void 报听规则_门清目标接近成型才允许锁定()
    {
        var sevenPairsEarly = new PursuitState(PursuitTarget.SevenPairs, true, 0.5, 0.5, 1.0, -1);
        var sevenPairsLate = new PursuitState(PursuitTarget.SevenPairs, true, 0.95, 0.95, 1.0, -1);
        var flush = new PursuitState(PursuitTarget.Flush, true, 0.6, 0.6, 1.0, (int)Suit.Pin);

        Assert.False(AiPursuit.AllowsCall(sevenPairsEarly, AiStyle.Default));
        Assert.True(AiPursuit.AllowsCall(sevenPairsLate, AiStyle.Default));
        Assert.True(AiPursuit.AllowsCall(flush, AiStyle.Default));
        Assert.True(AiPursuit.AllowsCall(PursuitState.Inactive, AiStyle.Default));
    }

    // ─────────────────────────────────────────────────────────────
    // 策略网：默认行为不变 + 温度采样
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void 未开启追役时_策略网仍走argmax()
    {
        Assert.True(NeuralPolicyAi.Available, "测试环境应能找到 ml/policy_weights_v4.json");

        var ctx = Context(Tiles("1m2m3m4m5m6m7m8m9m1p1p4p7s9s"), wall: 60);
        int legacy = NeuralPolicyAi.ChooseDiscard(ctx);
        int styled = NeuralPolicyAi.ChooseDiscard(
            ctx, AiStyle.PursuitFocused, PursuitState.Inactive, new FixedRandom(0.5));

        Assert.InRange(legacy, 0, ctx.Hand.Count - 1);
        Assert.Equal(legacy, styled);
    }

    [Fact]
    public void 追役状态_采样返回合法下标且同种子可复现()
    {
        var ctx = Context(Tiles("1m1m2m2m3m3m4m4m5p5p6p6p7s8s"), wall: 60);
        var pursuit = new PursuitState(PursuitTarget.SevenPairs, true, 0.86, 0.86, 1.0, -1);

        int first = NeuralPolicyAi.ChooseDiscard(ctx, AiStyle.Default, pursuit, new Mulberry32Random(7));
        int second = NeuralPolicyAi.ChooseDiscard(ctx, AiStyle.Default, pursuit, new Mulberry32Random(7));

        Assert.InRange(first, 0, ctx.Hand.Count - 1);
        Assert.Equal(first, second);
    }

    [Fact]
    public void 温度采样_均匀分数按累计概率落点()
    {
        double[] scores = { 0, 0, 0 };

        Assert.Equal(0, NeuralPolicyAi.SampleScores(scores, 1.0, new FixedRandom(0.0)));
        Assert.Equal(1, NeuralPolicyAi.SampleScores(scores, 1.0, new FixedRandom(0.5)));
        Assert.Equal(2, NeuralPolicyAi.SampleScores(scores, 1.0, new FixedRandom(0.9)));
    }

    [Fact]
    public void 温度采样_极低温度退化为argmax_非有限值也退argmax()
    {
        double[] peaked = { 1.0, 3.0, 2.0 };

        Assert.Equal(1, NeuralPolicyAi.SampleScores(peaked, 0.0, new FixedRandom(0.99)));
        Assert.Equal(1, NeuralPolicyAi.SampleScores(peaked, double.NaN, new FixedRandom(0.99)));

        double[] broken = { 0.0, double.NaN, 2.0 };
        Assert.Equal(2, NeuralPolicyAi.SampleScores(broken, 1.0, new FixedRandom(0.0)));
    }

    [Fact]
    public void 追役弃牌bonus_对子牌低于孤张()
    {
        var ctx = Context(Tiles("1m1m2m2m3m3m4m4m5p5p6p6p7s8s"), wall: 60);
        var state = new PursuitState(PursuitTarget.SevenPairs, true, 0.86, 0.86, 1.0, -1);

        double paired = AiPursuit.DiscardBonus(state, ctx, new Tile(Suit.Man, 1), AiStyle.Default);
        double isolated = AiPursuit.DiscardBonus(state, ctx, new Tile(Suit.Sou, 8), AiStyle.Default);

        Assert.True(isolated > paired);
        Assert.Equal(0.0, AiPursuit.DiscardBonus(PursuitState.Inactive, ctx, new Tile(Suit.Sou, 8), AiStyle.Default), 9);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private sealed class FixedRandom : IRandomSource
    {
        private readonly double _value;

        public FixedRandom(double value) => _value = value;

        public double NextDouble() => _value;
    }

    private static AiDecisionContext Context(
        List<Tile> hand,
        int wall = 60,
        int[]? scores = null,
        IReadOnlyList<Meld>? melds = null,
        int ankanCount = 0,
        SeatWind seat = SeatWind.East) =>
        new(
            hand,
            melds ?? Array.Empty<Meld>(),
            Array.Empty<Tile>(),
            NoRivers(),
            Array.Empty<Meld>(),
            0,
            wall,
            scores ?? new[] { 25000, 25000, 25000, 25000 },
            seat,
            SeatWind.East,
            ankanCount,
            0);

    private static IReadOnlyList<IReadOnlyList<Tile>> NoRivers() =>
        new[] { new List<Tile>(), new List<Tile>(), new List<Tile>(), new List<Tile>() };

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
