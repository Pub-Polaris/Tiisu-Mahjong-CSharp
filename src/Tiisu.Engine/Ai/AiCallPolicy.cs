using Tiisu.Core;
using CoreMeld = Tiisu.Core.Meld;

namespace Tiisu.Engine.Ai;

/// <summary>
/// 一次报听评估结果。<see cref="DiscardIndex"/> 是「打出这张后能报听」的下标；
/// 手牌本来就是 13 张形（荣和 / 副露窗口）时为 -1。
/// <para><see cref="ExpectedFu"/> 是荣和口径的期望赋：按待牌剩余张数加权，
/// 无役待牌按 0 赋计入分母（等价于「先按概率摸到能和的牌，再乘该手赋」）。</para>
/// </summary>
public readonly record struct AiCallDecision(
    bool Declare,
    int DiscardIndex,
    int WaitCount,
    double ExpectedFu,
    double Danger,
    ulong WaitMask)
{
    public static AiCallDecision None => new(false, -1, 0, 0, 0, 0);
}

/// <summary>
/// 报听（叫胡）决策（分档）。
///
/// <list type="bullet">
/// <item>Simple：永不报听。</item>
/// <item>Normal：听牌就报（只要打出一张后是听牌形）。</item>
/// <item>Hard：听牌 + 荣和口径期望赋 ≥ <see cref="AiTuning.HardCallMinExpectedFu"/>，
/// 且要打出的那张放铳危险度 ≤ <see cref="AiTuning.HardCallMaxDanger"/>。</item>
/// <item>Shura：同 Hard，但期望赋门槛与危险度门槛都更严。</item>
/// </list>
///
/// 只读 <see cref="AiDecisionContext"/>（该玩家可见的信息），不修改任何对局状态。
/// 报听后的锁手 / 禁吃碰由 Runtime 与引擎共同保证；本类只保证「选出的弃牌不会破坏听牌形」。
/// </summary>
public static class AiCallPolicy
{
    private static readonly Lazy<YakuData> YakuLazy = new(YakuData.LoadDefault);
    private static readonly Lazy<WinChecker> CheckerLazy = new(() => new WinChecker(YakuLazy.Value));

    /// <summary>
    /// 是否应该报听。<paramref name="preferenceOrder"/> 为空时按手牌顺序试所有弃牌；
    /// Runtime 会传入 AI 弃牌排名，取「排名最高且能报听」的那张。
    /// </summary>
    public static bool ShouldDeclareCall(
        AiDecisionContext ctx,
        AiLevel level,
        AiTuning? tuning = null,
        IReadOnlyList<int>? preferenceOrder = null) =>
        Evaluate(ctx, level, tuning, preferenceOrder).Declare;

    /// <summary>完整评估：是否报听、报听该打哪张、期望赋 / 危险度 / 听牌张掩码。</summary>
    public static AiCallDecision Evaluate(
        AiDecisionContext ctx,
        AiLevel level,
        AiTuning? tuning = null,
        IReadOnlyList<int>? preferenceOrder = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        tuning ??= AiTuning.Default;

        if (level == AiLevel.Simple || ctx.Hand.Count == 0)
        {
            return AiCallDecision.None;
        }

        int kongs = ctx.AnkanCount + ctx.MinkanCount;
        int meldTiles = 0;
        foreach (var meld in ctx.Melds)
        {
            meldTiles += meld.Tiles.Count;
        }

        int total = ctx.Hand.Count + meldTiles;
        bool is13 = total == 13 + kongs;
        bool is14 = total == 14 + kongs;
        if (!is13 && !is14)
        {
            return AiCallDecision.None;
        }

        // 14 张形先看向听：非听牌（向听 > 0）时任何弃牌都到不了听牌，省掉 14×34 次判役。
        if (is14 && kongs == 0 && Shanten.Calculate(ctx.Hand, ctx.Melds).Shanten > 0)
        {
            return AiCallDecision.None;
        }

        var rivers = TieredAi.Rivers(ctx);
        var threat = TieredAi.BuildThreatInput(ctx, rivers);

        if (is13)
        {
            var decision = Assess(ctx, ctx.Hand, -1, null, rivers, threat, level, tuning);
            return decision.Declare ? decision : AiCallDecision.None;
        }

        var order = preferenceOrder is { Count: > 0 }
            ? preferenceOrder
            : Enumerable.Range(0, ctx.Hand.Count).ToArray();

        var seen = new HashSet<int>();
        foreach (int index in order)
        {
            if (index < 0 || index >= ctx.Hand.Count || !seen.Add(index))
            {
                continue;
            }

            var rest = new List<Tile>(ctx.Hand.Count - 1);
            for (int i = 0; i < ctx.Hand.Count; i++)
            {
                if (i != index)
                {
                    rest.Add(ctx.Hand[i]);
                }
            }

            var decision = Assess(ctx, rest, index, ctx.Hand[index], rivers, threat, level, tuning);
            if (decision.Declare)
            {
                return decision;
            }
        }

        return AiCallDecision.None;
    }

    private static AiCallDecision Assess(
        AiDecisionContext ctx,
        IReadOnlyList<Tile> hand,
        int discardIndex,
        Tile? discarded,
        IReadOnlyList<IReadOnlyList<Tile>> rivers,
        ThreatInput threat,
        AiLevel level,
        AiTuning tuning)
    {
        var options = new TenpaiOptions
        {
            // 与引擎报听判定同口径（引擎 IsTenpai 用自摸口径）；期望赋另按荣和口径估计，
            // 避免自摸加成把「荣和根本和不了一手」的听牌估成高价值。
            SelfDraw = true,
            HasCalled = ctx.Melds.Count > 0 || ctx.AnkanCount > 0 || ctx.MinkanCount > 0,
            AnkanCount = ctx.AnkanCount,
            MinkanCount = ctx.MinkanCount,
            FirstTurn = ctx.FirstTurn,
            SeatWind = ctx.SeatWind,
            RoundWind = ctx.RoundWind,
            BaseFu = YakuLazy.Value.BaseFu,
        };

        List<TenpaiEntry> entries;
        try
        {
            entries = TenpaiCalculator.ListTenpai(CheckerLazy.Value, hand, ToCoreMelds(ctx.Melds), options);
        }
        catch
        {
            return AiCallDecision.None;
        }

        if (entries.Count == 0)
        {
            return AiCallDecision.None;
        }

        var visible = Ukeire.CountVisible(hand, ctx.Melds, rivers, ctx.OpponentMelds);
        if (discarded is Tile discardedTile && discardedTile.Index is >= 0 and < 36)
        {
            visible[discardedTile.Index]++;
        }

        int totalWaits = 0;
        int successWaits = 0;
        double successFu = 0;
        ulong mask = 0;
        foreach (var entry in entries)
        {
            // TotalFu == 0 表示这张加进来根本不成和形（listTenpai 的未成和行），不计入期望。
            if (entry.TotalFu <= 0)
            {
                continue;
            }

            int remaining = 4 - visible[entry.Tile.Index];
            if (remaining <= 0)
            {
                continue;
            }

            totalWaits += remaining;
            if (!entry.Success)
            {
                continue;
            }

            mask |= 1UL << entry.Tile.Index;
            successWaits += remaining;
            successFu += entry.TotalFu * remaining;
        }

        if (mask == 0 || totalWaits == 0 || successWaits == 0)
        {
            return AiCallDecision.None;
        }

        double expected = successFu / totalWaits;
        double danger = discarded is Tile tile
            ? ThreatModel.Danger(tile, threat)
            : MinDanger(hand, threat);

        double minExpected = level switch
        {
            AiLevel.Hard => tuning.HardCallMinExpectedFu,
            AiLevel.Shura => tuning.ShuraCallMinExpectedFu,
            _ => 0.0,
        };
        double maxDanger = level switch
        {
            AiLevel.Hard => tuning.HardCallMaxDanger,
            AiLevel.Shura => tuning.ShuraCallMaxDanger,
            _ => 1.0,
        };

        if (expected < minExpected || danger > maxDanger)
        {
            return AiCallDecision.None;
        }

        return new AiCallDecision(true, discardIndex, successWaits, expected, danger, mask);
    }

    private static double MinDanger(IReadOnlyList<Tile> hand, ThreatInput threat)
    {
        double min = double.MaxValue;
        foreach (var tile in hand)
        {
            double value = ThreatModel.Danger(tile, threat);
            if (value < min)
            {
                min = value;
            }
        }

        return min == double.MaxValue ? 0.0 : min;
    }

    private static List<CoreMeld> ToCoreMelds(IReadOnlyList<Meld> melds) =>
        melds.Select(m => new CoreMeld(m.Type.Wire(), m.Tiles.ToList())).ToList();
}
