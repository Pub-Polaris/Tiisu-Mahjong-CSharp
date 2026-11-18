using Tiisu.Localization;

namespace Tiisu.Engine;

/// <summary>牌工具（对应 tiles.js 的 sortHand / countTiles 与 engine.js 的牌面显示）。</summary>
internal static class TileUtil
{
    public const int Count = 36;

    /// <summary>JS <c>sortHand</c>：万 &lt; 饼 &lt; 索 &lt; 字；字牌按 东南西北白发中。Core 的 Index 顺序与之相同。</summary>
    public static void SortHand(List<Tile> hand) => hand.Sort();

    /// <summary>JS <c>countTiles</c>：按牌序号压成 36 格计数。</summary>
    public static int[] Counts(IEnumerable<Tile> tiles)
    {
        var counts = new int[Count];
        foreach (var tile in tiles)
        {
            counts[tile.Index]++;
        }

        return counts;
    }

    /// <summary>风牌（东南西北）。四风连打判定用。</summary>
    public static bool IsWind(Tile tile) => tile.Suit == Suit.Honor && tile.Num is >= 1 and <= 4;

    /// <summary>JS <c>tileDisplay</c>：只用于日志/流局理由文本。</summary>
    public static string Display(Tile tile) => tile.Suit switch
    {
        Suit.Man => L.T("engine.tile.man", ("num", tile.Num)),
        Suit.Pin => L.T("engine.tile.pin", ("num", tile.Num)),
        Suit.Sou => L.T("engine.tile.sou", ("num", tile.Num)),
        _ => tile.Num switch
        {
            1 => L.T("engine.tile.honor.east"),
            2 => L.T("engine.tile.honor.south"),
            3 => L.T("engine.tile.honor.west"),
            4 => L.T("engine.tile.honor.north"),
            5 => L.T("engine.tile.honor.white"),
            6 => L.T("engine.tile.honor.green"),
            7 => L.T("engine.tile.honor.red"),
            _ => tile.Num.ToString(),
        },
    };
}
