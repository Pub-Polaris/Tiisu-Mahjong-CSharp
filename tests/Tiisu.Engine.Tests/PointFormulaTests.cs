using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.Engine.Tests;

/// <summary>
/// 荣和点数公式（261112 复核）：表外赋（双碰）先并入 base 再乘庄家 1.5，本场奖励最后加。
/// 例：对对和3 + 缺门4 = 7 赋 → 7000；双碰2 表外 → +1400；庄家 ×1.5 → 12600；本场3 → +2100 = 14700。
/// </summary>
public sealed class PointFormulaTests
{
    private static MahjongEngine NewEngine(int honba, int[] scores)
    {
        var yaku = YakuData.LoadDefault();
        var engine = new MahjongEngine(checker: new WinChecker(yaku), yakuData: yaku, randomSource: new Mulberry32Random(1), ruleset: Ruleset.Qizi);
        engine.RestoreProgress(0, 21, 0, honba, 2, 8, scores);
        engine.UpdateSeatWinds();
        engine.Players[0].IsDealer = true;
        return engine;
    }

    private static WinSettlement Settlement()
    {
        var settlement = new WinSettlement { TotalFu = 9, HandType = HandKind.StandardHand };
        settlement.ActiveYaku.Add(new WinYaku { Name = "对对和", Fu = 3 });
        settlement.ActiveYaku.Add(new WinYaku { Name = "缺门", Fu = 4 });
        settlement.ActiveYaku.Add(new WinYaku { Name = "双碰", Fu = 2, IsBonus = true });
        return settlement;
    }

    [Fact]
    public void 庄家荣和_表外赋参与庄家加成_本场最后加()
    {
        var engine = NewEngine(honba: 3, scores: new[] { 95668, 68135, 58101, 86101 });

        engine.DeclareMultiRon(new[] { new RonWinner(0, Settlement()) }, 2);

        Assert.Equal(14700, engine.LastRonMulti[0].FinalPoints);
        Assert.Equal(95668 + 14700, engine.Scores[0]);
        Assert.Equal(58101 - 14700, engine.Scores[2]);
    }

    [Fact]
    public void 无本场时_庄家荣和_是12600()
    {
        var engine = NewEngine(honba: 0, scores: new[] { 50000, 50000, 50000, 50000 });

        engine.DeclareMultiRon(new[] { new RonWinner(0, Settlement()) }, 2);

        Assert.Equal(12600, engine.LastRonMulti[0].FinalPoints);
    }

    [Fact]
    public void 自摸分摊按350取整_余数进场供()
    {
        var yaku = YakuData.LoadDefault();
        var engine = new MahjongEngine(checker: new WinChecker(yaku), yakuData: yaku, randomSource: new Mulberry32Random(2), ruleset: Ruleset.Qizi);
        engine.RestoreProgress(0, 1, 0, honba: 0, roundWindIdx: 0, dealerCount: 0, scores: new[] { 49000, 49000, 49000, 49000 });
        engine.UpdateSeatWinds();

        var settlement = new WinSettlement { TotalFu = 2, HandType = HandKind.StandardHand };
        settlement.ActiveYaku.Add(new WinYaku { Name = "断幺九", Fu = 2 });

        var (each, remainder) = engine.SplitPayment(1400);
        Assert.Equal(350, each);
        Assert.Equal(350, remainder);

        int beforeTotal = engine.Scores.Sum();
        engine.DeclareWin(1, selfDraw: true, settlement);

        Assert.Equal(beforeTotal, engine.Scores.Sum());
        Assert.Equal(350, engine.PotPoints);
        Assert.Equal(each * 3, engine.Scores[1] - 49000);
    }

    [Fact]
    public void 下一手和牌拿走场供()
    {
        var yaku = YakuData.LoadDefault();
        var engine = new MahjongEngine(checker: new WinChecker(yaku), yakuData: yaku, randomSource: new Mulberry32Random(3), ruleset: Ruleset.Qizi);
        engine.RestoreProgress(0, 2, 0, honba: 0, roundWindIdx: 0, dealerCount: 0, scores: new[] { 49000, 49000, 49000, 49000 });
        engine.UpdateSeatWinds();
        engine.RestorePot(700);

        var settlement = new WinSettlement { TotalFu = 2, HandType = HandKind.StandardHand };
        settlement.ActiveYaku.Add(new WinYaku { Name = "断幺九", Fu = 2 });

        engine.DeclareWin(1, selfDraw: true, settlement);

        // 拿走旧场供 700，本手又产生 350 余数进场供
        Assert.Equal(350, engine.PotPoints);
        Assert.Equal(700 + 350 * 3, engine.Scores[1] - 49000);
    }
}
