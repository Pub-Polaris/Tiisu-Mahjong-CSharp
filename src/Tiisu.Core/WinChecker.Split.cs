namespace Tiisu.Core;

public sealed partial class WinChecker
{
    /// <summary>
    /// 枚举「n 面子 + 1 雀头」的全部拆法（供高目优先使用）。
    /// 与 JS splitStandardAll 完全同序：按 id 升序取第一张剩余牌，先试刻子、再试顺子。
    /// </summary>
    private static List<List<Group>> SplitStandardAll(IReadOnlyList<Tile> hand)
    {
        var result = new List<List<Group>>();
        int count = hand.Count;
        if (count < 2 || (count - 2) % 3 != 0)
        {
            return result;
        }

        int need = (count - 2) / 3;
        var counts = TileCounts.Create(hand);
        var ids = new List<int>();
        for (int i = 0; i < TileCounts.Length; i++)
        {
            if (counts[i] > 0)
            {
                ids.Add(i);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<Group>(need);
        var output = result;

        void Rec(int depth)
        {
            if (depth == need)
            {
                int leftId = -1;
                int leftKinds = 0;
                foreach (var id in ids)
                {
                    if (counts[id] > 0)
                    {
                        leftKinds++;
                        leftId = id;
                    }
                }

                if (leftKinds == 1 && counts[leftId] == 2)
                {
                    var pairTile = Tile.FromIndex(leftId);
                    var groups = new List<Group>(path) { new(GroupKind.Pair, new[] { pairTile, pairTile }) };
                    string key = SplitKey(groups);
                    if (seen.Add(key))
                    {
                        output.Add(groups);
                    }
                }

                return;
            }

            int first = -1;
            foreach (var id in ids)
            {
                if (counts[id] > 0)
                {
                    first = id;
                    break;
                }
            }

            if (first < 0)
            {
                return;
            }

            var tile = Tile.FromIndex(first);

            if (counts[first] >= 3)
            {
                counts[first] -= 3;
                path.Add(new Group(GroupKind.Pung, new[] { tile, tile, tile }));
                Rec(depth + 1);
                path.RemoveAt(path.Count - 1);
                counts[first] += 3;
            }

            if (!tile.IsHonor && tile.Num <= 7)
            {
                int secondIndex = first + 1;
                int thirdIndex = first + 2;
                if (counts[secondIndex] > 0 && counts[thirdIndex] > 0)
                {
                    counts[first]--;
                    counts[secondIndex]--;
                    counts[thirdIndex]--;
                    path.Add(new Group(GroupKind.Sequence, new[]
                    {
                        tile, Tile.FromIndex(secondIndex), Tile.FromIndex(thirdIndex),
                    }));
                    Rec(depth + 1);
                    path.RemoveAt(path.Count - 1);
                    counts[first]++;
                    counts[secondIndex]++;
                    counts[thirdIndex]++;
                }
            }
        }

        Rec(0);
        return result;
    }

    private static string SplitKey(IReadOnlyList<Group> groups)
    {
        var parts = new List<string>(groups.Count);
        foreach (var group in groups)
        {
            var ids = group.Tiles.Select(t => t.Id).ToList();
            ids.Sort(StringComparer.Ordinal);
            parts.Add(string.Concat(ids));
        }

        parts.Sort(StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    /// <summary>JS splitStandard：牌按 id 排序后找雀头，再用 splitGroups 递归拆面子（第一个成功解）。</summary>
    private static (bool Success, List<Group> Groups) SplitStandard(IReadOnlyList<Tile> hand)
    {
        var sorted = hand.ToList();
        sorted.Sort(static (a, b) => a.CompareTo(b));
        return FindPair(sorted);
    }

    private static (bool Success, List<Group> Groups) FindPair(List<Tile> tiles)
    {
        if (tiles.Count == 0)
        {
            return (true, new List<Group>());
        }

        if (tiles.Count % 3 != 2)
        {
            return (false, new List<Group>());
        }

        for (int i = 0; i < tiles.Count - 1; i++)
        {
            if (tiles[i].Id != tiles[i + 1].Id)
            {
                continue;
            }

            var rest = new List<Tile>(tiles.Count - 2);
            for (int k = 0; k < tiles.Count; k++)
            {
                if (k != i && k != i + 1)
                {
                    rest.Add(tiles[k]);
                }
            }

            var split = SplitGroups(rest);
            if (split.Success)
            {
                var groups = new List<Group> { new(GroupKind.Pair, new[] { tiles[i], tiles[i + 1] }) };
                groups.AddRange(split.Groups);
                return (true, groups);
            }
        }

        return (false, new List<Group>());
    }

    private static (bool Success, List<Group> Groups) SplitGroups(List<Tile> tiles)
    {
        if (tiles.Count == 0)
        {
            return (true, new List<Group>());
        }

        if (tiles.Count % 3 != 0)
        {
            return (false, new List<Group>());
        }

        var first = tiles[0];

        int firstSame = -1;
        for (int i = 1; i < tiles.Count; i++)
        {
            if (tiles[i].Id == first.Id)
            {
                firstSame = i;
                break;
            }
        }

        if (firstSame >= 0)
        {
            int secondSame = -1;
            for (int i = firstSame + 1; i < tiles.Count; i++)
            {
                if (tiles[i].Id == first.Id)
                {
                    secondSame = i;
                    break;
                }
            }

            if (secondSame >= 0)
            {
                var rest = new List<Tile>(tiles.Count - 3);
                for (int k = 0; k < tiles.Count; k++)
                {
                    if (k != 0 && k != firstSame && k != secondSame)
                    {
                        rest.Add(tiles[k]);
                    }
                }

                var split = SplitGroups(rest);
                if (split.Success)
                {
                    var groups = new List<Group>
                    {
                        new(GroupKind.Pung, new[] { first, tiles[firstSame], tiles[secondSame] }),
                    };
                    groups.AddRange(split.Groups);
                    return (true, groups);
                }
            }
        }

        if (!first.IsHonor)
        {
            int next1 = -1;
            int next2 = -1;
            for (int i = 1; i < tiles.Count; i++)
            {
                if (tiles[i].Suit == first.Suit && tiles[i].Num == first.Num + 1)
                {
                    next1 = i;
                    break;
                }
            }

            for (int i = 1; i < tiles.Count; i++)
            {
                if (tiles[i].Suit == first.Suit && tiles[i].Num == first.Num + 2)
                {
                    next2 = i;
                    break;
                }
            }

            if (next1 >= 0 && next2 >= 0)
            {
                var rest = new List<Tile>(tiles.Count - 3);
                for (int k = 0; k < tiles.Count; k++)
                {
                    if (k != 0 && k != next1 && k != next2)
                    {
                        rest.Add(tiles[k]);
                    }
                }

                var split = SplitGroups(rest);
                if (split.Success)
                {
                    var groups = new List<Group>
                    {
                        new(GroupKind.Sequence, new[] { first, tiles[next1], tiles[next2] }),
                    };
                    groups.AddRange(split.Groups);
                    return (true, groups);
                }
            }
        }

        return (false, new List<Group>());
    }

    /// <summary>
    /// 听牌型奖赏（5.5）。优先级：嵌张 &gt; 边张 &gt; 单钓 &gt; 双碰 &gt; 筋和。
    /// 双碰/单钓在和牌张为幺九或字牌时各再加 1 赋。
    /// </summary>
    private static YakuResult? WaitFuOf(IReadOnlyList<Tile> hand, IReadOnlyList<Group> groups, Tile? winTile)
    {
        if (winTile is null || groups.Count == 0)
        {
            return null;
        }

        var win = winTile.Value;
        var counts = TileCounts.Create(hand);
        var usedSequences = new HashSet<string>(StringComparer.Ordinal);
        var usedPairs = new HashSet<string>(StringComparer.Ordinal);
        var usedPungs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            if (!group.Tiles.Any(t => t.Id == win.Id))
            {
                continue;
            }

            string face = GroupFace(group);
            if (group.Kind == GroupKind.Sequence)
            {
                usedSequences.Add(face);
            }
            else if (group.Kind == GroupKind.Pair)
            {
                usedPairs.Add(face);
            }
            else
            {
                usedPungs.Add(face);
            }
        }

        // 1) 嵌张：顺子里的中间张，且另两张各有 2 张（和牌前是 35 听 4 的形）
        foreach (var face in usedSequences)
        {
            var parts = face.Split('|');
            char suit = parts[0][0];
            var nums = parts.Skip(1).Select(int.Parse).ToArray();
            if (win.IsHonor || nums[1] != win.Num)
            {
                continue;
            }

            if (CountAt(counts, suit, nums[0]) >= 2 && CountAt(counts, suit, nums[2]) >= 2)
            {
                return new YakuResult { Name = "嵌张", Fu = 1, IsBonus = true };
            }
        }

        // 2) 边张：12 听 3 / 89 听 7
        foreach (var face in usedSequences)
        {
            if (win.IsHonor)
            {
                continue;
            }

            var parts = face.Split('|');
            char suit = parts[0][0];
            var nums = parts.Skip(1).Select(int.Parse).ToArray();
            int index = Array.IndexOf(nums, win.Num);
            bool strict = (nums[0] == 1 && nums[1] == 2 && nums[2] == 3 && win.Num == 3 && index == 2)
                || (nums[0] == 7 && nums[1] == 8 && nums[2] == 9 && win.Num == 7 && index == 0);
            if (!strict)
            {
                continue;
            }

            if (CountAt(counts, suit, nums[0]) >= 1 && CountAt(counts, suit, nums[1]) >= 1)
            {
                return new YakuResult { Name = "边张", Fu = 1, IsBonus = true };
            }
        }

        // 3/4) 单钓 / 双碰：必须看和牌前的 13 张才能区分
        if (usedPairs.Count > 0 || usedPungs.Count > 0)
        {
            string? preWin = SplitPreWin(hand, win);
            if (preWin == "single")
            {
                return new YakuResult { Name = "单钓", Fu = 1 + YaochuBonus(win), IsBonus = true };
            }

            if (preWin == "shanpon")
            {
                return new YakuResult { Name = "双碰", Fu = 1 + YaochuBonus(win), IsBonus = true };
            }
        }

        // 5) 筋和（剩余情形：两面等）
        return new YakuResult { Name = "筋和", Fu = 1, IsBonus = true };
    }

    /// <summary>
    /// 判断和牌前的 13 张（去掉一张和牌张）的形状：
    /// single = 4 面子 + 1 张孤张；shanpon = 3 面子 + 2 个对子；否则 null。
    /// </summary>
    private static string? SplitPreWin(IReadOnlyList<Tile> hand, Tile winTile)
    {
        var arr = hand.ToList();
        int position = arr.FindIndex(t => t.Id == winTile.Id);
        if (position < 0)
        {
            return null;
        }

        arr.RemoveAt(position);
        var counts = TileCounts.Create(arr);
        var pairIds = new List<Tile>();
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 2)
            {
                pairIds.Add(Tile.FromIndex(i));
            }
        }

        // 双碰：3 面子 + 2 对子
        if (pairIds.Count >= 2)
        {
            for (int i = 0; i < pairIds.Count - 1; i++)
            {
                for (int j = i + 1; j < pairIds.Count; j++)
                {
                    var rest = arr.Where(t => t.Id != pairIds[i].Id && t.Id != pairIds[j].Id).ToList();
                    var split = SplitGroups(rest);
                    if (split.Success && split.Groups.Count == 3)
                    {
                        return "shanpon";
                    }
                }
            }
        }

        // 单钓：4 面子 + 1 张孤张（逐个候选试）
        for (int k = 0; k < arr.Count; k++)
        {
            var rest = new List<Tile>(arr.Count - 1);
            for (int i = 0; i < arr.Count; i++)
            {
                if (i != k)
                {
                    rest.Add(arr[i]);
                }
            }

            var split = SplitGroups(rest);
            if (split.Success && split.Groups.Count == 4)
            {
                return "single";
            }
        }

        return null;
    }

    private static string GroupFace(Group group)
    {
        char suit = group.First.Suit switch
        {
            Suit.Man => 'm',
            Suit.Pin => 'p',
            Suit.Sou => 's',
            _ => 'z',
        };
        return suit + "|" + string.Join("|", group.Tiles.Select(t => t.Num));
    }

    private static int CountAt(int[] counts, char suit, int num) =>
        counts[TileCounts.Index(CharSuit(suit), num)];

    private static Suit CharSuit(char suit) => suit switch
    {
        'm' => Suit.Man,
        'p' => Suit.Pin,
        's' => Suit.Sou,
        _ => Suit.Honor,
    };

    /// <summary>和牌张为幺九或字牌时，双碰/单钓各 +1 赋。</summary>
    private static int YaochuBonus(Tile tile) => tile.IsHonor || tile.Num is 1 or 9 ? 1 : 0;
}
