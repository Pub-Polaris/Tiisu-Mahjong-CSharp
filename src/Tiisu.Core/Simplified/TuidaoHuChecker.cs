using System;
using System.Collections.Generic;
using System.Linq;

namespace Tiisu.Core;

/// <summary>推倒胡的六种和牌形态（2026-11-02 立项，项目代号 1102工程）。</summary>
public enum TuidaoHuShape
{
    /// <summary>不成和。</summary>
    None = 0,

    /// <summary>四顺一对。</summary>
    FourSequencesPair,

    /// <summary>一刻三顺一对。</summary>
    OneTripletThreeSequencesPair,

    /// <summary>二刻二顺一对。</summary>
    TwoTripletsTwoSequencesPair,

    /// <summary>一顺三刻一对。</summary>
    OneSequenceThreeTripletsPair,

    /// <summary>四刻一对（碰碰胡）。</summary>
    FourTripletsPair,

    /// <summary>七对：必须 7 种不同牌各一对（四张同牌不能当两对）。</summary>
    SevenPairs,

    /// <summary>十三幺：恰好 19m19p19s1234567z 这 13 种，再多其中任意一张作对子；必须门前。</summary>
    ThirteenOrphans,
}

/// <summary>
/// 推倒胡判定器（独立于七子的 <see cref="WinChecker"/>）：
/// 只判形态，不看役、不算赋、不设起和线。
/// <para>
/// 口径（北极星 2026-11-02）：
/// 一般型 5 种 = 四顺一对 / 一刻三顺一对 / 二刻二顺一对 / 一顺三刻一对 / 四刻一对；
/// 七对 = 7 种不同牌各一对（龙七对不算）；
/// 十三幺 = 恰好 13 种幺九字牌 + 其中一张作对子，且必须门前（不能有副露）。
/// </para>
/// </summary>
public static class TuidaoHuChecker
{
    private const int TileTypeCount = 34;

    /// <summary>判一手牌是不是推倒胡和牌，并给出形态。</summary>
    /// <param name="hand">暗手牌（含和牌张）；有副露时 = 14 + 杠数 − 副露张数。</param>
    /// <param name="melds">副露；七对 / 十三幺 必须为空。</param>
    public static TuidaoHuShape Classify(IReadOnlyList<Tile> hand, IReadOnlyList<Meld>? melds = null)
    {
        ArgumentNullException.ThrowIfNull(hand);
        melds ??= Array.Empty<Meld>();

        int kongCount = melds.Count(m => m.Tiles.Count == 4);
        if (hand.Count + melds.Sum(m => m.Tiles.Count) != 14 + kongCount)
        {
            return TuidaoHuShape.None;
        }

        if (melds.Count == 0 && IsThirteenOrphans(hand))
        {
            return TuidaoHuShape.ThirteenOrphans;
        }

        if (melds.Count == 0 && IsSevenPairs(hand))
        {
            return TuidaoHuShape.SevenPairs;
        }

        return ClassifyStandard(hand, melds, kongCount);
    }

    /// <summary>是否推倒胡和牌。</summary>
    public static bool IsWin(IReadOnlyList<Tile> hand, IReadOnlyList<Meld>? melds = null) =>
        Classify(hand, melds) != TuidaoHuShape.None;

    // ── 一般型 ───────────────────────────────────────────────────

    private static TuidaoHuShape ClassifyStandard(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, int kongCount)
    {
        int meldPungs = 0;
        foreach (var meld in melds)
        {
            if (!TryMeldPung(meld, out bool pung))
            {
                return TuidaoHuShape.None;
            }

            if (pung)
            {
                meldPungs++;
            }
        }

        int needSets = 4 - melds.Count;
        if (needSets < 0)
        {
            return TuidaoHuShape.None;
        }

        var counts = new int[TileTypeCount];
        foreach (var tile in hand)
        {
            counts[tile.Index]++;
        }

        // 选雀头，再拆剩下的面子；拆法只要成功一种即可。
        for (int pair = 0; pair < TileTypeCount; pair++)
        {
            if (counts[pair] < 2)
            {
                continue;
            }

            counts[pair] -= 2;
            int concealedPungs = 0;
            bool ok = SplitSets(counts, needSets, ref concealedPungs);
            counts[pair] += 2;

            if (!ok)
            {
                continue;
            }

            int pungs = meldPungs + concealedPungs;
            if (pungs > 4)
            {
                continue;
            }

            return pungs switch
            {
                0 => TuidaoHuShape.FourSequencesPair,
                1 => TuidaoHuShape.OneTripletThreeSequencesPair,
                2 => TuidaoHuShape.TwoTripletsTwoSequencesPair,
                3 => TuidaoHuShape.OneSequenceThreeTripletsPair,
                _ => TuidaoHuShape.FourTripletsPair,
            };
        }

        return TuidaoHuShape.None;
    }

    /// <summary>副露的面子类型：吃=顺子，碰/明杠/暗杠/加杠=刻子；未知类型视为不合法。</summary>
    private static bool TryMeldPung(Meld meld, out bool pung)
    {
        pung = false;
        switch (meld.Type)
        {
            case "chi":
            case "sequence":
                return meld.Tiles.Count == 3;
            case "pon":
            case "pung":
            case "minkan":
            case "ankan":
            case "kakan":
                pung = true;
                return meld.Tiles.Count is 3 or 4;
            default:
                return false;
        }
    }

    /// <summary>把 counts 拆成 <paramref name="needSets"/> 个面子（顺子或刻子）；成功时回填刻子数。</summary>
    private static bool SplitSets(int[] counts, int needSets, ref int pungs)
    {
        if (needSets == 0)
        {
            return counts.All(c => c == 0);
        }

        int first = -1;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0)
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return false;
        }

        var tile = Tile.FromIndex(first);

        // 刻子
        if (counts[first] >= 3)
        {
            counts[first] -= 3;
            pungs++;
            bool ok = SplitSets(counts, needSets - 1, ref pungs);
            if (ok)
            {
                return true;
            }

            pungs--;
            counts[first] += 3;
        }

        // 顺子（只能数牌，且不能跨花色）
        if (!tile.IsHonor && tile.Num <= 7
            && counts[first + 1] > 0 && counts[first + 2] > 0)
        {
            counts[first]--;
            counts[first + 1]--;
            counts[first + 2]--;
            bool ok = SplitSets(counts, needSets - 1, ref pungs);
            counts[first]++;
            counts[first + 1]++;
            counts[first + 2]++;
            if (ok)
            {
                return true;
            }
        }

        return false;
    }

    // ── 特殊型 ───────────────────────────────────────────────────

    /// <summary>七对：7 种不同牌各 2 张；四张同牌（4 张）不算两对。</summary>
    private static bool IsSevenPairs(IReadOnlyList<Tile> hand)
    {
        if (hand.Count != 14)
        {
            return false;
        }

        var counts = new int[TileTypeCount];
        foreach (var tile in hand)
        {
            counts[tile.Index]++;
        }

        int pairs = 0;
        foreach (int c in counts)
        {
            if (c == 0)
            {
                continue;
            }

            if (c != 2)
            {
                return false;
            }

            pairs++;
        }

        return pairs == 7;
    }

    /// <summary>十三幺：只允许 19m19p19s1234567z 这 13 种，且正好 13 种各一张 + 其中一张重复一次。</summary>
    private static bool IsThirteenOrphans(IReadOnlyList<Tile> hand)
    {
        if (hand.Count != 14)
        {
            return false;
        }

        var counts = new int[TileTypeCount];
        foreach (var tile in hand)
        {
            if (!IsThirteenOrphansType(tile))
            {
                return false;
            }

            counts[tile.Index]++;
        }

        int distinct = 0;
        foreach (int c in counts)
        {
            if (c == 0)
            {
                continue;
            }

            if (c > 2)
            {
                return false;
            }

            distinct++;
        }

        return distinct == 13;
    }

    private static bool IsThirteenOrphansType(Tile tile) =>
        tile.IsHonor || tile.Num is 1 or 9;
}
