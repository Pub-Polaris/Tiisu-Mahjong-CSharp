namespace Tiisu.Engine.Ai;

/// <summary>一张有效进张：牌、剩余张数、摸到后的向听。</summary>
public sealed record UkeireEntry(Tile Tile, int Remaining, ShantenResult After);

/// <summary>进张计算结果。</summary>
public sealed record UkeireResult(
    ShantenResult Current,
    IReadOnlyList<UkeireEntry> Entries,
    int TotalRemaining);

/// <summary>
/// 进张（ukeire）计算：枚举 34 种牌，找出「摸到后向听数下降」的有效牌，
/// 并按四张扣除可见牌（自家手牌 + 自家 / 别家副露 + 四家牌河）。
///
/// <para>
/// 只在**只读可见信息**上统计，不读别家手牌与牌山。手牌应当是 3n+1 张（通常 13 张）；
/// 若传入 3n+2 张，向听语义是「打完一张之后」，结果不作为进张口径使用。
/// </para>
/// </summary>
public static class Ukeire
{
    private const int VisibleSlots = 36;
    private const int StandardKindCount = 34;

    /// <summary>
    /// 计算进张。
    /// </summary>
    /// <param name="hand">暗手（不含副露）。</param>
    /// <param name="melds">自家副露 / 杠。</param>
    /// <param name="allDiscards">四家牌河（含自家），顺序无关。</param>
    /// <param name="current">已知的当前向听；null 时现算。</param>
    /// <param name="opponentMelds">别家副露（只读可见信息，可空）。</param>
    public static UkeireResult Calculate(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        IReadOnlyList<IReadOnlyList<Tile>> allDiscards,
        ShantenResult? current = null,
        IReadOnlyList<Meld>? opponentMelds = null)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(melds);
        ArgumentNullException.ThrowIfNull(allDiscards);

        var now = current ?? Shanten.Calculate(hand, melds);
        var visible = CountVisible(hand, melds, allDiscards, opponentMelds);

        var entries = new List<UkeireEntry>();
        int total = 0;

        for (int i = 0; i < StandardKindCount; i++)
        {
            int remaining = 4 - visible[i];
            if (remaining <= 0)
            {
                continue;
            }

            var candidate = Tile.FromIndex(i);
            var trial = new List<Tile>(hand.Count + 1);
            trial.AddRange(hand);
            trial.Add(candidate);

            var after = Shanten.Calculate(trial, melds);
            if (after.Shanten < now.Shanten)
            {
                entries.Add(new UkeireEntry(candidate, remaining, after));
                total += remaining;
            }
        }

        return new UkeireResult(now, entries, total);
    }

    /// <summary>可见张数（按牌序号 36 格）：自家手牌 + 全部副露 + 四家牌河。</summary>
    public static int[] CountVisible(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        IReadOnlyList<IReadOnlyList<Tile>> allDiscards,
        IReadOnlyList<Meld>? opponentMelds = null)
    {
        var visible = new int[VisibleSlots];

        foreach (var tile in hand)
        {
            visible[tile.Index]++;
        }

        foreach (var meld in melds)
        {
            foreach (var tile in meld.Tiles)
            {
                visible[tile.Index]++;
            }
        }

        if (opponentMelds is not null)
        {
            foreach (var meld in opponentMelds)
            {
                foreach (var tile in meld.Tiles)
                {
                    visible[tile.Index]++;
                }
            }
        }

        foreach (var river in allDiscards)
        {
            foreach (var tile in river)
            {
                visible[tile.Index]++;
            }
        }

        return visible;
    }
}
