namespace Tiisu.Engine;

/// <summary>
/// 一位玩家（JS <c>TiisuPlayer</c>）。索引的牌一律是 Core 的 <see cref="Tile"/> 值类型；
/// 相同牌不可区分，所以"删某张牌"等价于 JS 的 <c>indexOf/splice</c>。
/// </summary>
public sealed class Player
{
    public Player(int index, string name)
    {
        Index = index;
        Name = name;
    }

    public int Index { get; }

    public string Name { get; }

    public bool IsHuman { get; set; }

    public bool IsDealer { get; set; }

    public SeatWind SeatWind { get; set; }

    public List<Tile> Hand { get; } = new();

    public List<Tile> Discards { get; } = new();

    public List<Meld> Melds { get; } = new();

    public List<Tile> Kans { get; } = new();

    public int AnkanCount { get; set; }

    public int MinkanCount { get; set; }

    public bool FirstTurn { get; set; } = true;

    public Tile? LastDraw { get; set; }

    /// <summary>本摸为倒数第二张墙牌（海上月）。</summary>
    public bool SeaMoon { get; set; }

    /// <summary>本摸为最后一张墙牌（海底月）。</summary>
    public bool HaiDei { get; set; }

    /// <summary>本摸来自岭上（岭上花）。</summary>
    public bool RinshanDrawn { get; set; }

    /// <summary>本局第一张打出后是否已听牌（水滴石破）。</summary>
    public bool FirstTurnTenpai { get; set; }

    /// <summary>副露/杠后免常规摸牌。</summary>
    public bool SkipDraw { get; set; }

    /// <summary>JS <c>totalTiles()</c>：暗手 + 副露张数。</summary>
    public int TotalTiles()
    {
        int meldTiles = 0;
        foreach (var meld in Melds)
        {
            meldTiles += meld.Tiles.Count;
        }

        return Hand.Count + meldTiles;
    }

    /// <summary>JS <c>kongCount()</c>：4 张面子即杠。</summary>
    public int KongCount()
    {
        int count = 0;
        foreach (var meld in Melds)
        {
            if (meld.IsKong)
            {
                count++;
            }
        }

        return count;
    }

    public void AddMeld(MeldType type, IEnumerable<Tile> tiles)
    {
        Melds.Add(new Meld(type, tiles));
        if (type is MeldType.Minkan or MeldType.Kakan)
        {
            MinkanCount++;
        }

        if (type == MeldType.Ankan)
        {
            AnkanCount++;
        }
    }

    public Meld? FindPonMeld(Tile tile)
    {
        foreach (var meld in Melds)
        {
            if (meld.Type == MeldType.Pon && meld.Tiles.Count > 0 && meld.Tiles[0] == tile)
            {
                return meld;
            }
        }

        return null;
    }

    /// <summary>JS <c>reset()</c>：清空本局牌面，不动姓名/门风/庄标记。</summary>
    public void Reset()
    {
        Hand.Clear();
        Discards.Clear();
        Melds.Clear();
        Kans.Clear();
        AnkanCount = 0;
        MinkanCount = 0;
        FirstTurn = true;
        LastDraw = null;
        SeaMoon = false;
        HaiDei = false;
        RinshanDrawn = false;
        FirstTurnTenpai = false;
        SkipDraw = false;
    }
}
