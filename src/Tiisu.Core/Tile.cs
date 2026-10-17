namespace Tiisu.Core;

/// <summary>牌的花色。数牌三种 + 字牌一种，顺序与 JS 侧 rank 一致。</summary>
public enum Suit
{
    Man = 0,
    Pin = 1,
    Sou = 2,
    Honor = 3,
}

/// <summary>
/// 一张牌。id 规则与 Web 侧一致：数牌 <c>m1</c>/<c>p9</c>/<c>s5</c>，字牌 <c>z1</c>~<c>z7</c>。
/// 字牌的 <see cref="Num"/> 同样归一为 1~7（JS 侧字牌 num 是字符串，这里用 int 并在需要处显式区分）。
/// </summary>
public readonly record struct Tile(Suit Suit, int Num) : IComparable<Tile>
{
    /// <summary>
    /// 牌序号：m1..m9=0..8，p1..p9=9..17，s1..s9=18..26，z1..z9=27..35。
    /// 字牌保留 9 格是因为 golden 语料用 z9 表示一个“普通字牌”（JS 侧 num 为字符串 "9"）。
    /// </summary>
    public int Index => Suit == Suit.Honor ? 27 + Num - 1 : (int)Suit * 9 + Num - 1;
    public bool IsHonor => Suit == Suit.Honor;

    public bool IsTerminal => !IsHonor && (Num == 1 || Num == 9);

    public string Id => Suit switch
    {
        Suit.Man => "m",
        Suit.Pin => "p",
        Suit.Sou => "s",
        _ => "z",
    } + Num;

    public static Tile Parse(string id)
    {
        if (!TryParse(id, out var tile))
        {
            throw new FormatException($"Invalid tile id: {id}");
        }

        return tile;
    }

    public static bool TryParse(string? id, out Tile tile)
    {
        tile = default;
        if (string.IsNullOrEmpty(id) || id.Length < 2)
        {
            return false;
        }

        Suit suit = char.ToLowerInvariant(id[0]) switch
        {
            'm' => Suit.Man,
            'p' => Suit.Pin,
            's' => Suit.Sou,
            'z' => Suit.Honor,
            _ => (Suit)(-1),
        };
        if ((int)suit < 0)
        {
            return false;
        }

        if (!int.TryParse(id.AsSpan(1), out int num))
        {
            return false;
        }

        // 语料中存在测试用的 z9（JS 用 {suit:'z', num:'9'} 表示一个“普通字牌”），因此字牌也允许 1~9。
        int max = 9;
        if (num < 1 || num > max)
        {
            return false;
        }

        tile = new Tile(suit, num);
        return true;
    }

    public static Tile FromIndex(int index)
    {
        if (index is < 0 or >= 36)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index >= 27
            ? new Tile(Suit.Honor, index - 26)
            : new Tile((Suit)(index / 9), index % 9 + 1);
    }

    public int CompareTo(Tile other) => Index.CompareTo(other.Index);

    public override string ToString() => Id;
}

/// <summary>工具：把牌列表压成 34 格计数。</summary>
internal static class TileCounts
{
    public const int Length = 36;

    public static int[] Create(IEnumerable<Tile> tiles)
    {
        var counts = new int[Length];
        foreach (var tile in tiles)
        {
            counts[tile.Index]++;
        }

        return counts;
    }

    public static int Index(Suit suit, int num) =>
        suit == Suit.Honor ? 27 + num - 1 : (int)suit * 9 + num - 1;
}
