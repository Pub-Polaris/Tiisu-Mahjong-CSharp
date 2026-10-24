using Tiisu.Core;

namespace Tiisu.Engine;

/// <summary>listTenpai 的一行结果（tenpai.js 的 <c>{tile, success, totalFu, noYakuGap}</c>）。</summary>
public sealed record TenpaiEntry(Tile Tile, bool Success, int TotalFu, int NoYakuGap)
{
    /// <summary>
    /// 牌形成和（<c>true</c> = 加这张能拆成和牌形）。<c>no_yaku</c> 行也算 <c>true</c>——
    /// 赋数不够起和线只是不能和，牌形本身是听着的。报听可用性 / 锁手只看这个标志。
    /// </summary>
    public bool Formed { get; init; }
}

/// <summary>listTenpai 的入参（engine.js 里给 <c>listTenpai</c> 的第二层 opts）。</summary>
public sealed class TenpaiOptions
{
    public bool SelfDraw { get; init; } = true;

    public bool HasCalled { get; init; }

    public int AnkanCount { get; init; }

    public int MinkanCount { get; init; }

    public bool FirstTurn { get; init; }

    public SeatWind SeatWind { get; init; }

    public SeatWind RoundWind { get; init; }

    /// <summary>起和门槛（ike.json 的 baseFu），无役听牌的差赋用它算。</summary>
    public int BaseFu { get; init; } = 7;
}

/// <summary>
/// 听牌判定（tenpai.js 的移植）：对 34 种牌逐一模拟加入暗手，返回可和牌与 no_yaku 差赋。
/// 判役复用 <see cref="WinChecker"/>，与 Web 侧同一套口径。
/// </summary>
public static class TenpaiCalculator
{
    public static List<TenpaiEntry> ListTenpai(
        WinChecker checker,
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Tiisu.Core.Meld> melds,
        TenpaiOptions options)
    {
        var result = new List<TenpaiEntry>();
        for (int i = 0; i < 34; i++)
        {
            var candidate = Tile.FromIndex(i);
            int used = 0;
            foreach (var tile in hand)
            {
                if (tile == candidate)
                {
                    used++;
                }
            }

            foreach (var meld in melds)
            {
                foreach (var tile in meld.Tiles)
                {
                    if (tile == candidate)
                    {
                        used++;
                    }
                }
            }

            if (used >= 4)
            {
                continue;
            }

            var win = CheckCandidate(checker, hand, melds, options, candidate);
            if (win is null)
            {
                continue;
            }

            int noYakuGap = win.Reason == "no_yaku" ? options.BaseFu - (win.TotalFu ?? 0) : 0;
            result.Add(new TenpaiEntry(candidate, win.Success, win.TotalFu ?? 0, noYakuGap)
            {
                Formed = win.Reason != "not_formed",
            });
        }

        return result;
    }

    /// <summary>
    /// 对单一候选张做一次「加进暗手后能否和」的判役，口径与 <see cref="ListTenpai"/> 完全一致（同一份
    /// <see cref="TenpaiOptions"/>、同一个 <see cref="WinContext"/>）。判役异常时返回 null。
    /// <para>
    /// <paramref name="selfDraw"/> 为空时沿用 <paramref name="options.SelfDraw"/>；传 false 可按荣和口径判，
    /// 用来只数「和牌本身成立的役」——自摸 2 赋 / 卡七自摸加成这类靠自摸才加的赋不是役。
    /// </para>
    /// </summary>
    public static WinResult? CheckCandidate(
        WinChecker checker,
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Tiisu.Core.Meld> melds,
        TenpaiOptions options,
        Tile candidate,
        bool? selfDraw = null)
    {
        var trial = new List<Tile>(hand.Count + 1);
        trial.AddRange(hand);
        trial.Add(candidate);

        try
        {
            return checker.CheckWin(
                trial,
                selfDraw ?? options.SelfDraw,
                options.HasCalled,
                options.AnkanCount,
                options.MinkanCount,
                melds,
                options.FirstTurn,
                candidate,
                new WinContext
                {
                    SeaMoon = false,
                    HaiDei = false,
                    RiverFish = false,
                    LastTileVisible = 0,
                    SeatWind = options.SeatWind.Wire(),
                    RoundWind = options.RoundWind.Wire(),
                });
        }
        catch
        {
            return null;
        }
    }
}
