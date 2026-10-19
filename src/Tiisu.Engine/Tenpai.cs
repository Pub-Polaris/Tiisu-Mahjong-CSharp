using Tiisu.Core;

namespace Tiisu.Engine;

/// <summary>listTenpai 的一行结果（tenpai.js 的 <c>{tile, success, totalFu, noYakuGap}</c>）。</summary>
public sealed record TenpaiEntry(Tile Tile, bool Success, int TotalFu, int NoYakuGap);

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

            var trial = new List<Tile>(hand.Count + 1);
            trial.AddRange(hand);
            trial.Add(candidate);

            WinResult win;
            try
            {
                win = checker.CheckWin(
                    trial,
                    options.SelfDraw,
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
                continue;
            }

            int noYakuGap = win.Reason == "no_yaku" ? options.BaseFu - (win.TotalFu ?? 0) : 0;
            result.Add(new TenpaiEntry(candidate, win.Success, win.TotalFu ?? 0, noYakuGap));
        }

        return result;
    }
}
