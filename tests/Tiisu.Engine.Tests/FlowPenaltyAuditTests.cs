using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.Engine.Tests;

/// <summary>
/// 流局罚项审计（261112）：结构化转账明细必须能配平实际分数变化，
/// 否则 LastFlowPenaltyAudit 会给出差异（同时由 App 写 tiisu-error.log）。
/// </summary>
public sealed class FlowPenaltyAuditTests
{
    private static List<Tile> T(string spec)
    {
        var result = new List<Tile>();
        var digits = new List<char>();
        foreach (char ch in spec)
        {
            if (char.IsDigit(ch)) { digits.Add(ch); continue; }
            Suit suit = ch switch { 'm' => Suit.Man, 'p' => Suit.Pin, 's' => Suit.Sou, 'z' => Suit.Honor, _ => Suit.Man };
            foreach (char d in digits) { result.Add(new Tile(suit, d - '0')); }
            digits.Clear();
        }
        return result;
    }

    private static void Meld(Player p, MeldType type, string tiles) => p.AddMeld(type, T(tiles));

    private static MahjongEngine NewEngine(int[] scores, int dealer, int honba = 0)
    {
        var yaku = YakuData.LoadDefault();
        var engine = new MahjongEngine(checker: new WinChecker(yaku), yakuData: yaku, randomSource: new Mulberry32Random(1), ruleset: Ruleset.Qizi);
        engine.RestoreProgress(0, 1, dealer, honba, 0, 0, scores);
        engine.UpdateSeatWinds();
        return engine;
    }

    private static void DealTestHands(MahjongEngine engine)
    {
        engine.Players[0].Hand.AddRange(T("123m456m789m11p2z"));
        engine.Players[1].Hand.AddRange(T("1357m2468p19s2z"));
        Meld(engine.Players[1], MeldType.Minkan, "1111s");
        engine.Players[2].Hand.AddRange(T("234m345p456s678s1z"));
        engine.Players[3].Hand.AddRange(T("345m456p678s789s2z"));
    }

    private static void AssertLedgerBalances(MahjongEngine engine, int[] before)
    {
        Assert.Empty(engine.LastFlowPenaltyAudit);
        var expected = new int[4];
        foreach (var transfer in engine.LastFlowTransfers)
        {
            expected[transfer.From] -= transfer.Amount;
            expected[transfer.To] += transfer.Amount;
        }

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(expected[i], engine.Scores[i] - before[i]);
        }
    }

    [Fact]
    public void 未听返杠_账本与分数一致()
    {
        var engine = NewEngine(new[] { 25000, 25000, 25000, 25000 }, dealer: 0);
        DealTestHands(engine);
        var before = engine.Scores.ToArray();

        engine.Flow();

        Assert.NotEmpty(engine.LastFlowTransfers);
        AssertLedgerBalances(engine, before);
    }

}
