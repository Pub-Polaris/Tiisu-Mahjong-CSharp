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
}
