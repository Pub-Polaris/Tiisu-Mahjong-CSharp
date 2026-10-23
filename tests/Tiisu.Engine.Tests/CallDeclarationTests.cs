using Tiisu.Core;
using Tiisu.Engine;

namespace Tiisu.Engine.Tests;

/// <summary>
/// 报听（叫胡）机制：宣言、锁手（听牌张集合不变）、暗杠例外、禁吃碰、
/// 普通 / 重罚错和、本局 / 下局禁和、流局翻牌与「报听者自动算听牌」。
/// 规则口径见 <c>docs/DSH/报听机制_261023.md</c>。
/// </summary>
public sealed class CallDeclarationTests
{
    /// <summary>手牌记号：<c>1111m23m456m789m5z</c>（数字后跟花色，可多段）。</summary>
    private static List<Tile> Hand(string spec)
    {
        var tiles = new List<Tile>();
        var numbers = new List<int>();
        foreach (char ch in spec)
        {
            if (ch is >= '0' and <= '9')
            {
                numbers.Add(ch - '0');
                continue;
            }

            Suit suit = ch switch
            {
                'm' => Suit.Man,
                'p' => Suit.Pin,
                's' => Suit.Sou,
                _ => Suit.Honor,
            };
            foreach (int n in numbers)
            {
                tiles.Add(new Tile(suit, n));
            }

            numbers.Clear();
        }

        return tiles;
    }

    /// <summary>
    /// 开一局：自家用给定 13 张手牌，其他三家固定杂牌。
    /// 基准听牌形 <c>1111m23m456m789m5z</c>：111m + 123m + 456m + 789m，单钓 5z；
    /// 混一色 + 缺门 ≥ 起和线，因此是引擎口径下的「真听牌」。
    /// </summary>
    private static MahjongEngine NewRound(string selfHand, int dealer, out Player player)
    {
        var engine = new MahjongEngine();
        var hands = new List<IReadOnlyList<Tile>>
        {
            Hand(selfHand),
            Hand("1234m6789p12345s"),
            Hand("6789m1234p6789s1z"),
            Hand("2345m6789p2345s1z"),
        };

        engine.StartNewRound(new RoundDeal(dealer, 0, hands));
        player = engine.Players[0];
        return engine;
    }

    private const string TenpaiHand = "1111m23m456m789m5z";

    private static MahjongEngine NewRound(out Player player) => NewRound(TenpaiHand, 0, out player);

    [Fact]
    public void DeclareCall_LocksWaits_WhenTenpai()
    {
        var engine = NewRound(out var player);

        Assert.True(engine.IsTenpai(player));
        Assert.True(engine.DeclareCall(player));
        Assert.True(player.IsCalled);
        Assert.NotEqual(0UL, player.CalledWaitMask);

        // 听牌张集合 = 单钓白（5z，index 31）
        Assert.Equal(1UL << 31, player.CalledWaitMask);
    }

    [Fact]
    public void DeclareCall_Rejected_WhenNotTenpai()
    {
        var engine = NewRound("1234m6789p12345s", 0, out var player);

        Assert.False(engine.IsTenpai(player));
        Assert.False(engine.DeclareCall(player));
        Assert.False(player.IsCalled);
    }

    [Fact]
    public void DeclareCall_Rejected_WhenAlreadyCalled()
    {
        var engine = NewRound(out var player);

        Assert.True(engine.DeclareCall(player));
        Assert.False(engine.DeclareCall(player));
    }

    [Fact]
    public void DeclareCall_Rejected_WhenRoundEnded()
    {
        var engine = NewRound(out var player);
        engine.DeclareSpecialFlow("test");

        Assert.True(engine.GameOver);
        Assert.False(engine.DeclareCall(player));
    }

    [Fact]
    public void Discard_ThatChangesWaits_IsRejected()
    {
        var engine = NewRound(out var player);
        Assert.True(engine.DeclareCall(player));
        engine.Draw(player, new Tile(Suit.Pin, 9)); // 摸到杂牌 9p（14 张形）

        // 打 1m 会拆掉 123m，听牌张不再是 5z → 拒绝
        var oneMan = new Tile(Suit.Man, 1);
        Assert.False(engine.CallAllowsDiscard(player, oneMan, out string? reason));
        Assert.Contains("听牌张", reason ?? string.Empty);

        int handBefore = player.Hand.Count;
        Assert.Null(engine.Discard(player, oneMan));
        Assert.Equal(handBefore, player.Hand.Count);

        // 打摸到的 9p 回到原听牌形 → 允许
        Assert.True(engine.CallAllowsDiscard(player, new Tile(Suit.Pin, 9), out _));
        Assert.NotNull(engine.Discard(player, new Tile(Suit.Pin, 9)));
    }

    [Fact]
    public void ChiPonMinkanKakan_AreForbidden_AfterCall()
    {
        var engine = NewRound(out var player);
        Assert.True(engine.DeclareCall(player));

        Assert.False(engine.CallAllowsMeld(player, MeldType.Chi, out _));
        Assert.False(engine.CallAllowsMeld(player, MeldType.Pon, out _));
        Assert.False(engine.CallAllowsMeld(player, MeldType.Minkan, out _));
        Assert.False(engine.CallAllowsMeld(player, MeldType.Kakan, out _));

        // 规则层硬拒绝：真的调副露会抛（Runtime 会先拦，这里是兜底）
        Assert.Throws<InvalidOperationException>(() =>
            engine.ApplyMeld(0, MeldType.Pon, new Tile(Suit.Man, 1), 1));

        // 加杠被 DoKan 拒绝（返回 false，不改手牌）
        player.AddMeld(MeldType.Pon, new[] { new Tile(Suit.Honor, 5), new Tile(Suit.Honor, 5), new Tile(Suit.Honor, 5) });
        Assert.False(engine.DoKan(player, new Tile(Suit.Honor, 5), MeldType.Kakan));
    }

    [Fact]
    public void Ankan_Allowed_WhenWaitsUnchanged()
    {
        var engine = NewRound(out var player);
        Assert.True(engine.DeclareCall(player));

        // 摸 4m：暗杠 1m 后 23m 由 4m 补成 234m，听牌张仍是 5z → 形不变，允许
        engine.Draw(player, new Tile(Suit.Man, 4));
        Assert.True(engine.CallAllowsAnkan(player, new Tile(Suit.Man, 1), out string? reason), reason);
    }

    [Fact]
    public void Ankan_Rejected_WhenWaitsChange()
    {
        var engine = NewRound(out var player);
        Assert.True(engine.DeclareCall(player));

        // 摸杂牌 9p：暗杠 1m 会拆掉 123m，听牌张从 5z 掉到无 → 拒绝
        engine.Draw(player, new Tile(Suit.Pin, 9));
        Assert.False(engine.CallAllowsAnkan(player, new Tile(Suit.Man, 1), out string? reason));
        Assert.Contains("暗杠", reason ?? string.Empty);

        // 手里没有四张同样的牌
        Assert.False(engine.CallAllowsAnkan(player, new Tile(Suit.Man, 2), out _));
    }

    [Fact]
    public void Misfire_Minor_PaysFuTimes700_ToEachOpponent()
    {
        var engine = NewRound(out var player);
        Assert.True(engine.DeclareCall(player));
        var before = engine.Scores.ToArray();

        // 赋 2 → Z = 1400（≤ 2100，普通错和）
        var penalty = engine.ApplyCallMisfire(0, 2);

        Assert.NotNull(penalty);
        Assert.False(penalty!.Severe);
        Assert.Equal(1400, penalty.Z);
        Assert.Equal(before[0] - 4200, engine.Scores[0]);
        Assert.Equal(before[1] + 1400, engine.Scores[1]);
        Assert.Equal(before[2] + 1400, engine.Scores[2]);
        Assert.Equal(before[3] + 1400, engine.Scores[3]);

        // 本次和牌不成立、解除报听、本巡禁和
        Assert.False(player.IsCalled);
        Assert.True(engine.IsWinBanned(player));
        Assert.False(engine.IsWinBannedNextHand(0));
        Assert.False(engine.GameOver); // 错和不结束本局
    }

    [Fact]
    public void Misfire_Minor_Boundary_Z2100_IsNotSevere()
    {
        var engine = NewRound(out var player);
        Assert.True(engine.DeclareCall(player));
        var before = engine.Scores.ToArray();

        // 赋 3 → Z = 2100：刚好不超过 2100，仍是普通错和
        var penalty = engine.ApplyCallMisfire(0, 3);

        Assert.NotNull(penalty);
        Assert.False(penalty!.Severe);
        Assert.Equal(2100, penalty.Z);
        Assert.Equal(before[0] - 6300, engine.Scores[0]);
        Assert.False(engine.IsWinBannedNextHand(0));
    }

    [Fact]
    public void Misfire_Severe_ChildPaysDealer14000Others7000()
    {
        var engine = NewRound(TenpaiHand, dealer: 1, out var player);
        Assert.True(engine.DeclareCall(player));
        var before = engine.Scores.ToArray();

        // 赋 4 → Z = 2800 > 2100 → 重罚：庄家（座位 1）14000，其余两家各 7000
        var penalty = engine.ApplyCallMisfire(0, 4);

        Assert.NotNull(penalty);
        Assert.True(penalty!.Severe);
        Assert.Equal(2800, penalty.Z);
        Assert.Equal(before[0] - 28000, engine.Scores[0]);
        Assert.Equal(before[1] + 14000, engine.Scores[1]);
        Assert.Equal(before[2] + 7000, engine.Scores[2]);
        Assert.Equal(before[3] + 7000, engine.Scores[3]);

        // 本局 + 下局禁和，解除报听
        Assert.False(player.IsCalled);
        Assert.True(engine.IsWinBanned(player));
        Assert.True(engine.IsWinBannedNextHand(0));
    }

    [Fact]
    public void Misfire_Severe_DealerPays7000Each()
    {
        var engine = NewRound(TenpaiHand, dealer: 0, out var player);
        Assert.True(engine.DeclareCall(player));
        var before = engine.Scores.ToArray();

        // 庄家自己违规：三家各 7000（文档未给数值，取子家档的一半）
        var penalty = engine.ApplyCallMisfire(0, 5);

        Assert.NotNull(penalty);
        Assert.True(penalty!.Severe);
        Assert.Equal(before[0] - 21000, engine.Scores[0]);
        Assert.Equal(before[1] + 7000, engine.Scores[1]);
        Assert.Equal(before[2] + 7000, engine.Scores[2]);
        Assert.Equal(before[3] + 7000, engine.Scores[3]);
    }

    [Fact]
    public void Misfire_NotApplied_WhenNotCalled_OrFuReachesBase()
    {
        var engine = NewRound(out var player);

        // 未报听：不算错和
        Assert.Null(engine.ApplyCallMisfire(0, 2));

        Assert.True(engine.DeclareCall(player));

        // 赋 ≥ 起和线：不是错和
        Assert.Null(engine.ApplyCallMisfire(0, engine.BaseFu));
        Assert.True(player.IsCalled);

        // 赋 0 也无意义
        Assert.Null(engine.ApplyCallMisfire(0, 0));
    }

    [Fact]
    public void WinBan_RotatesNextHand_AndClearsAfter()
    {
        var engine = NewRound(TenpaiHand, dealer: 1, out var player);
        Assert.True(engine.DeclareCall(player));
        engine.ApplyCallMisfire(0, 6); // 重罚 → 本局 + 下局禁和

        Assert.True(engine.IsWinBanned(player));
        Assert.True(engine.IsWinBannedNextHand(0));

        // 下一局：下局禁和转成本局禁和
        engine.StartNewRound(new RoundDeal(1, 0, new List<IReadOnlyList<Tile>>
        {
            Hand(TenpaiHand),
            Hand("1234m6789p12345s"),
            Hand("6789m1234p6789s1z"),
            Hand("2345m6789p2345s1z"),
        }));
        Assert.True(engine.IsWinBanned(engine.Players[0]));
        Assert.False(engine.IsWinBannedNextHand(0));

        // 再下一局：完全解除
        engine.StartNewRound(new RoundDeal(1, 0, new List<IReadOnlyList<Tile>>
        {
            Hand(TenpaiHand),
            Hand("1234m6789p12345s"),
            Hand("6789m1234p6789s1z"),
            Hand("2345m6789p2345s1z"),
        }));
        Assert.False(engine.IsWinBanned(engine.Players[0]));
    }

    [Fact]
    public void RoundReset_ClearsCalled()
    {
        var engine = NewRound(out var player);
        Assert.True(engine.DeclareCall(player));

        engine.StartNewRound(new RoundDeal(0, 0, new List<IReadOnlyList<Tile>>
        {
            Hand(TenpaiHand),
            Hand("1234m6789p12345s"),
            Hand("6789m1234p6789s1z"),
            Hand("2345m6789p2345s1z"),
        }));

        Assert.False(engine.Players[0].IsCalled);
        Assert.Equal(0UL, engine.Players[0].CalledWaitMask);
    }

    [Fact]
    public void Flow_CountsCalledPlayerAsTenpai_AndMarksOthersNoten()
    {
        var engine = NewRound(out var player);
        Assert.True(engine.DeclareCall(player));

        // 座位 3 未听牌但已报听 → 流局自动算听牌（不用再申报）
        engine.Players[3].IsCalled = true;

        engine.Flow();

        Assert.True(engine.RevealHands);
        Assert.Equal(4, engine.FlowTenpai.Count);
        Assert.True(engine.FlowTenpai[0]);   // 报听 + 真听牌
        Assert.True(engine.FlowTenpai[3]);   // 报听但手牌不听 → 仍算听牌
        Assert.False(engine.FlowTenpai[1]);  // 未报听 + 不听 → 灰显
        Assert.False(engine.FlowTenpai[2]);
    }

    [Fact]
    public void Flow_WithoutCall_UsesRealTenpai()
    {
        var engine = NewRound(out _);

        engine.Flow();

        Assert.Equal(4, engine.FlowTenpai.Count);
        Assert.True(engine.FlowTenpai[0]);   // 手牌真听牌
        Assert.False(engine.FlowTenpai[1]);
    }

    [Fact]
    public void WaitMaskFor_MatchesPlayerWaits()
    {
        var engine = NewRound(out var player);

        ulong mask = engine.WaitMaskFor(
            player.Hand, player.Melds, player.AnkanCount, player.MinkanCount, player.SeatWind, player.FirstTurn);

        Assert.Equal(engine.WaitMaskOf(player), mask);
        Assert.Equal(1UL << 31, mask);
    }
}
