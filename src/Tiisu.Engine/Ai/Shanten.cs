namespace Tiisu.Engine.Ai;

/// <summary>向听数的牌型类别。</summary>
public enum ShantenKind
{
    /// <summary>标准形：4 面子 + 1 雀头（副露 / 杠按已完成面子计）。</summary>
    Standard,

    /// <summary>七对子。</summary>
    SevenPairs,

    /// <summary>国士无双。</summary>
    Kokushi,
}

/// <summary>向听结果。0 = 听牌，-1 = 已和牌。</summary>
public readonly record struct ShantenResult(int Shanten, ShantenKind Kind);

/// <summary>
/// 向听数计算（A 期纯函数）：标准形 / 七对子 / 国士三者取最小。
///
/// <para>
/// 输入是**暗手**（不含副露的牌），副露 / 杠只通过 <c>meldCount</c> 参与：
/// 每副露（含杠）算一个已完成面子。因此 4 副露时暗手只应有 1~2 张。
/// </para>
/// <para>
/// 七对子口径与本作 <c>WinChecker.IsSevenPairs</c> 一致：四张同牌按 <b>2 对</b> 计，
/// 不额外要求 7 种不同牌（本作允许同一张牌组成两对）。因此直接用
/// <c>6 - min(7, Σ floor(count/2))</c>，不需要「种类数」修正。
/// </para>
/// </summary>
public static class Shanten
{
    private const int TileKindSlots = 36;
    private const int StandardKindCount = 34;

    /// <summary>
    /// 计算向听。
    /// </summary>
    /// <param name="hand">暗手牌（不含副露 / 杠的牌）。</param>
    /// <param name="meldCount">副露数（明杠 / 暗杠 / 加杠都算 1 个面子）。</param>
    /// <param name="kanCount">杠数，仅用于语义说明；标准形里杠已经包含在 <paramref name="meldCount"/> 中。</param>
    public static ShantenResult Calculate(IReadOnlyList<Tile> hand, int meldCount = 0, int kanCount = 0)
    {
        ArgumentNullException.ThrowIfNull(hand);
        if (meldCount is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(meldCount), meldCount, "副露数必须在 0..4");
        }

        _ = kanCount;

        var counts = new int[TileKindSlots];
        foreach (var tile in hand)
        {
            if (tile.Index is >= 0 and < TileKindSlots)
            {
                counts[tile.Index]++;
            }
        }

        int standard = StandardShanten(counts, meldCount);
        int sevenPairs = meldCount == 0 ? SevenPairsShanten(counts) : int.MaxValue;
        int kokushi = meldCount == 0 ? KokushiShanten(counts) : int.MaxValue;

        var best = new ShantenResult(standard, ShantenKind.Standard);
        if (sevenPairs < best.Shanten)
        {
            best = new ShantenResult(sevenPairs, ShantenKind.SevenPairs);
        }

        if (kokushi < best.Shanten)
        {
            best = new ShantenResult(kokushi, ShantenKind.Kokushi);
        }

        return best;
    }

    /// <summary>按副露列表计算（杠自动计入面子数）。</summary>
    public static ShantenResult Calculate(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(melds);

        int kanCount = 0;
        foreach (var meld in melds)
        {
            if (meld.IsKong)
            {
                kanCount++;
            }
        }

        return Calculate(hand, melds.Count, kanCount);
    }

    // ─────────────────────────────────────────────────────────────
    // 标准形
    // ─────────────────────────────────────────────────────────────

    private static int StandardShanten(int[] counts, int meldCount)
    {
        int best = 8;
        SearchStandard(counts, 0, meldCount, 0, 0, 0, ref best);
        return best;
    }

    /// <summary>
    /// 标准形 DFS：在 34 种牌上依次尝试「刻子 / 顺子 / 雀头 / 对子搭子 / 两面・嵌张搭子 / 跳过一张」。
    /// 到末尾用 8 - 2×面子 - 搭子 - 雀头 计算（搭子数按剩余面子数封顶）。
    /// </summary>
    private static void SearchStandard(
        int[] counts,
        int index,
        int meldCount,
        int sets,
        int partials,
        int pair,
        ref int best)
    {
        if (best == -1 || meldCount + sets > 4)
        {
            return;
        }

        if (index >= StandardKindCount)
        {
            int cappedPartials = Math.Min(partials, 4 - meldCount - sets);
            if (cappedPartials < 0)
            {
                cappedPartials = 0;
            }

            int value = 8 - 2 * (meldCount + sets) - cappedPartials - pair;

            // 14 张（3n+2）时「4 面子 + 搭子」会被公式算成 -1，但实际要打一张才是听牌。
            // 这里统一把「没有雀头的 -1」收敛到 0；真正的和牌（4 面子 + 雀头）仍是 -1。
            if (value < 0 && pair == 0)
            {
                value = 0;
            }

            if (value < best)
            {
                best = value;
            }

            return;
        }

        if (counts[index] == 0)
        {
            SearchStandard(counts, index + 1, meldCount, sets, partials, pair, ref best);
            return;
        }

        int groups = meldCount + sets;

        // 刻子
        if (counts[index] >= 3)
        {
            counts[index] -= 3;
            SearchStandard(counts, index, meldCount, sets + 1, partials, pair, ref best);
            counts[index] += 3;
        }

        // 顺子（仅数牌，且起始点 <= 7）
        if (index < 27 && index % 9 <= 6 && counts[index + 1] > 0 && counts[index + 2] > 0)
        {
            counts[index]--;
            counts[index + 1]--;
            counts[index + 2]--;
            SearchStandard(counts, index, meldCount, sets + 1, partials, pair, ref best);
            counts[index]++;
            counts[index + 1]++;
            counts[index + 2]++;
        }

        // 雀头
        if (pair == 0 && counts[index] >= 2)
        {
            counts[index] -= 2;
            SearchStandard(counts, index, meldCount, sets, partials, 1, ref best);
            counts[index] += 2;
        }

        if (groups < 4)
        {
            // 对子搭子
            if (counts[index] >= 2)
            {
                counts[index] -= 2;
                SearchStandard(counts, index, meldCount, sets, partials + 1, pair, ref best);
                counts[index] += 2;
            }

            if (index < 27)
            {
                int num = index % 9;

                // 两面 / 边张搭子
                if (num <= 7 && counts[index + 1] > 0)
                {
                    counts[index]--;
                    counts[index + 1]--;
                    SearchStandard(counts, index, meldCount, sets, partials + 1, pair, ref best);
                    counts[index]++;
                    counts[index + 1]++;
                }

                // 嵌张搭子
                if (num <= 6 && counts[index + 2] > 0)
                {
                    counts[index]--;
                    counts[index + 2]--;
                    SearchStandard(counts, index, meldCount, sets, partials + 1, pair, ref best);
                    counts[index]++;
                    counts[index + 2]++;
                }
            }
        }

        // 跳过这一张（作为孤张 / 将来再配）
        counts[index]--;
        SearchStandard(counts, index, meldCount, sets, partials, pair, ref best);
        counts[index]++;
    }

    // ─────────────────────────────────────────────────────────────
    // 七对子 / 国士
    // ─────────────────────────────────────────────────────────────

    private static int SevenPairsShanten(int[] counts)
    {
        int pairs = 0;
        for (int i = 0; i < StandardKindCount; i++)
        {
            pairs += counts[i] / 2;
        }

        if (pairs > 7)
        {
            pairs = 7;
        }

        return 6 - pairs;
    }

    private static readonly (Suit Suit, int Num)[] KokushiTiles =
    {
        (Suit.Man, 1), (Suit.Man, 9),
        (Suit.Pin, 1), (Suit.Pin, 9),
        (Suit.Sou, 1), (Suit.Sou, 9),
        (Suit.Honor, 1), (Suit.Honor, 2), (Suit.Honor, 3), (Suit.Honor, 4),
        (Suit.Honor, 5), (Suit.Honor, 6), (Suit.Honor, 7),
    };

    private static int KokushiShanten(int[] counts)
    {
        int kinds = 0;
        bool hasPair = false;
        foreach (var (suit, num) in KokushiTiles)
        {
            int index = suit == Suit.Honor ? 27 + num - 1 : (int)suit * 9 + num - 1;
            int count = counts[index];
            if (count > 0)
            {
                kinds++;
                if (count >= 2)
                {
                    hasPair = true;
                }
            }
        }

        return 13 - kinds - (hasPair ? 1 : 0);
    }
}
