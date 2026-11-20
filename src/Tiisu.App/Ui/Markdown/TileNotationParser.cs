using System;
using System.Collections.Generic;
using Tiisu.App.Runtime;
using Tiisu.Core;

namespace Tiisu.App.Ui.Markdown;

/// <summary>代码 span 内的一张牌；Id 为 null 时表示只占额外间距的空位。</summary>
public readonly record struct TileRun(string? Id, float ExtraGapBefore = 0f);

/// <summary>
/// 反引号内的牌型文本解析。
/// <para>
/// 只处理牌型 multiset，不推断面子拆分；<c>123333p</c> 是 1p + 2p + 3p×4，
/// 不猜 <c>12p+3333p</c> 或 <c>123p+333p</c>。
/// </para>
/// </summary>
public static class TileNotationParser
{
    private static readonly HashSet<char> Separators = new() { ' ', '\t', '/', '|', '-' };

    public static bool TryParse(string? text, out IReadOnlyList<TileRun> runs)
    {
        runs = Array.Empty<TileRun>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var result = new List<TileRun>();
        var counts = new int[34];
        float pendingGap = 0f;
        bool any = false;
        int i = 0;

        while (i < text.Length)
        {
            char ch = text[i];
            if (ch == '_')
            {
                pendingGap += 2f;
                i++;
                continue;
            }

            if (Separators.Contains(ch))
            {
                i++;
                continue;
            }

            char? meld = ch is 'c' or 'b' or 'a' or 'k' ? ch : null;
            if (meld is not null)
            {
                i++;
            }

            int digitStart = i;
            while (i < text.Length && text[i] is >= '0' and <= '9')
            {
                i++;
            }

            if (i == digitStart || i >= text.Length)
            {
                return false;
            }

            Suit suit = text[i] switch
            {
                'm' => Suit.Man,
                'p' => Suit.Pin,
                's' => Suit.Sou,
                'z' => Suit.Honor,
                _ => (Suit)(-1),
            };
            if (!Enum.IsDefined(suit))
            {
                return false;
            }

            i++;
            var tiles = new List<Tile>();
            for (int d = digitStart; d < i - 1; d++)
            {
                int num = text[d] - '0';
                if (num is < 1 or > 9 || (suit == Suit.Honor && num > 7))
                {
                    return false;
                }

                var tile = new Tile(suit, num);
                if (++counts[tile.Index] > 4)
                {
                    return false;
                }

                tiles.Add(tile);
            }

            if (meld is char prefix && !ValidMeld(prefix, suit, tiles))
            {
                return false;
            }

            bool first = true;
            foreach (var tile in tiles)
            {
                result.Add(new TileRun(TileNotation.AtlasId(tile), first ? pendingGap : 0f));
                pendingGap = 0f;
                first = false;
                any = true;
            }
        }

        if (pendingGap > 0f)
        {
            result.Add(new TileRun(null, pendingGap));
        }

        runs = result;
        return any;
    }

    private static bool ValidMeld(char prefix, Suit suit, IReadOnlyList<Tile> tiles)
    {
        switch (prefix)
        {
            case 'c':
                if (tiles.Count != 3 || suit == Suit.Honor)
                {
                    return false;
                }

                int low = Math.Min(tiles[0].Num, Math.Min(tiles[1].Num, tiles[2].Num));
                int high = Math.Max(tiles[0].Num, Math.Max(tiles[1].Num, tiles[2].Num));
                return high - low == 2
                    && tiles[0].Num != tiles[1].Num
                    && tiles[1].Num != tiles[2].Num
                    && tiles[0].Num != tiles[2].Num;
            case 'b':
                return tiles.Count == 3 && tiles[1] == tiles[0] && tiles[2] == tiles[0];
            case 'a':
            case 'k':
                // 文档里的 a222z / k333p 也常按三张写；真正的四张写法 a2222z / k3333p 同样接受。
                return tiles.Count is 3 or 4
                    && tiles[1] == tiles[0]
                    && tiles[2] == tiles[0]
                    && (tiles.Count == 3 || tiles[3] == tiles[0]);
            default:
                return false;
        }
    }
}
