namespace Tiisu.Core;

public sealed partial class WinChecker
{
    /// <summary>
    /// JS <c>conditionFn(id)</c> 的只读入口（调试页 / scoretest 用）。
    /// 与 JS 一样读取最近一次 <see cref="CheckWin"/> 留下的 <c>_winTile</c> / <c>_winCtx</c> 状态。
    /// </summary>
    public bool ConditionFn(string id, IReadOnlyList<Tile> hand, IReadOnlyList<Group>? groups = null) =>
        Evaluate(id, hand, groups);

    /// <summary>
    /// 判据分派。除 menzen_tsumo / tsumo 外，与 JS conditionFn 的键一一对应。
    /// 返回 true 表示该役成立；未知 id 返回 false（JS 中为 undefined → 不成立）。
    /// </summary>
    private bool Evaluate(string id, IReadOnlyList<Tile> hand, IReadOnlyList<Group>? groups)
    {
        switch (id)
        {
            case "menzen_tsumo":
            case "tsumo":
                return false;

            case "all_simple":
                foreach (var tile in hand)
                {
                    if (tile.IsHonor || tile.Num == 1 || tile.Num == 9)
                    {
                        return false;
                    }
                }

                return true;

            case "all_pungs":
                return groups is not null && groups.All(g => g.Kind != GroupKind.Sequence);

            case "half_flush":
                return IsHalfFlush(hand);

            case "fully_flush":
                return IsFullyFlush(hand);

            case "mixed_terminals":
                foreach (var tile in hand)
                {
                    if (tile.IsHonor)
                    {
                        continue;
                    }

                    if (tile.Num != 1 && tile.Num != 9)
                    {
                        return false;
                    }
                }

                return true;

            case "five_gates":
                return IsFiveGates(hand);

            case "same_high":
            {
                if (groups is null)
                {
                    return false;
                }

                var sequences = groups.Where(g => g.Kind == GroupKind.Sequence).ToList();
                if (sequences.Count < 2)
                {
                    return false;
                }

                for (int i = 0; i < sequences.Count - 1; i++)
                {
                    for (int j = i + 1; j < sequences.Count; j++)
                    {
                        if (sequences[i].First.Suit == sequences[j].First.Suit
                            && sequences[i].First.Num == sequences[j].First.Num)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            case "double_same_high":
            {
                if (groups is null)
                {
                    return false;
                }

                var sequences = groups.Where(g => g.Kind == GroupKind.Sequence).ToList();
                if (sequences.Count < 4)
                {
                    return false;
                }

                var counts = new Dictionary<(Suit Suit, int Num), int>();
                foreach (var sequence in sequences)
                {
                    var key = (sequence.First.Suit, sequence.First.Num);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }

                int pairs = counts.Values.Count(c => c >= 2);
                return pairs >= 2;
            }

            case "four_same":
                return TileCounts.Create(hand).Any(c => c == 4);

            case "two_color_straight":
            {
                if (groups is null)
                {
                    return false;
                }

                var sequences = groups.Where(g => g.Kind == GroupKind.Sequence).ToList();
                if (sequences.Count < 2)
                {
                    return false;
                }

                for (int i = 0; i < sequences.Count - 1; i++)
                {
                    for (int j = i + 1; j < sequences.Count; j++)
                    {
                        if (sequences[i].First.Suit != sequences[j].First.Suit
                            && sequences[i].First.Num == sequences[j].First.Num)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            case "one_color_triple":
            {
                if (groups is null)
                {
                    return false;
                }

                var pungs = groups
                    .Where(g => g.Kind == GroupKind.Pung && !g.First.IsHonor)
                    .ToList();
                if (pungs.Count < 3)
                {
                    return false;
                }

                for (int i = 0; i < pungs.Count - 2; i++)
                {
                    for (int j = i + 1; j < pungs.Count - 1; j++)
                    {
                        for (int k = j + 1; k < pungs.Count; k++)
                        {
                            var a = pungs[i].First;
                            var b = pungs[j].First;
                            var c = pungs[k].First;
                            if (a.Suit == b.Suit && b.Suit == c.Suit)
                            {
                                var nums = new[] { a.Num, b.Num, c.Num };
                                Array.Sort(nums);
                                if (nums[0] + 1 == nums[1] && nums[1] + 1 == nums[2])
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }

                return false;
            }

            case "two_ankou":
                return ConcealedAnkouCount() >= 2;

            case "three_ankou":
                return ConcealedAnkouCount() >= 3;

            case "missing_suit":
            {
                var suits = new HashSet<Suit>();
                foreach (var tile in hand)
                {
                    if (!tile.IsHonor)
                    {
                        suits.Add(tile.Suit);
                    }
                }

                return suits.Count <= 2;
            }

            case "two_color_ko":
            {
                if (groups is null)
                {
                    return false;
                }

                var pungs = groups.Where(g => g.Kind == GroupKind.Pung).ToList();
                if (pungs.Count < 2)
                {
                    return false;
                }

                var suits = new HashSet<Suit>();
                var nums = new List<int>();
                foreach (var pung in pungs)
                {
                    var tile = pung.First;
                    // JS 侧字牌 num 是字符串，与数牌 num 严格不等；含字牌刻子时二色同刻必不成立。
                    if (tile.IsHonor)
                    {
                        return false;
                    }

                    suits.Add(tile.Suit);
                    nums.Add(tile.Num);
                }

                return suits.Count == 2 && nums.All(n => n == nums[0]);
            }

            case "three_color_ko":
            {
                if (groups is null)
                {
                    return false;
                }

                var pungs = groups
                    .Where(g => g.Kind == GroupKind.Pung && !g.First.IsHonor)
                    .ToList();
                if (pungs.Count < 3)
                {
                    return false;
                }

                for (int i = 0; i < pungs.Count - 2; i++)
                {
                    for (int j = i + 1; j < pungs.Count - 1; j++)
                    {
                        for (int k = j + 1; k < pungs.Count; k++)
                        {
                            var a = pungs[i].First;
                            var b = pungs[j].First;
                            var c = pungs[k].First;
                            if (a.Suit != b.Suit && b.Suit != c.Suit && a.Suit != c.Suit
                                && a.Num == b.Num && b.Num == c.Num)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            case "three_color_straight":
            {
                if (groups is null)
                {
                    return false;
                }

                var sequences = groups.Where(g => g.Kind == GroupKind.Sequence).ToList();
                if (sequences.Count < 3)
                {
                    return false;
                }

                for (int i = 0; i < sequences.Count - 2; i++)
                {
                    for (int j = i + 1; j < sequences.Count - 1; j++)
                    {
                        for (int k = j + 1; k < sequences.Count; k++)
                        {
                            var a = sequences[i].First;
                            var b = sequences[j].First;
                            var c = sequences[k].First;
                            if (a.Suit != b.Suit && b.Suit != c.Suit && a.Suit != c.Suit
                                && a.Num == b.Num && b.Num == c.Num)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            case "one_color_step":
            {
                if (groups is null)
                {
                    return false;
                }

                var sequences = groups.Where(g => g.Kind == GroupKind.Sequence).ToList();
                if (sequences.Count < 3)
                {
                    return false;
                }

                for (int i = 0; i < sequences.Count - 2; i++)
                {
                    for (int j = i + 1; j < sequences.Count - 1; j++)
                    {
                        for (int k = j + 1; k < sequences.Count; k++)
                        {
                            var a = sequences[i].First;
                            var b = sequences[j].First;
                            var c = sequences[k].First;
                            if (a.Suit == b.Suit && b.Suit == c.Suit)
                            {
                                var nums = new[] { a.Num, b.Num, c.Num };
                                Array.Sort(nums);
                                if (nums[0] + 1 == nums[1] && nums[1] + 1 == nums[2])
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }

                return false;
            }

            case "one_dragon":
            {
                if (groups is null)
                {
                    return false;
                }

                var bySuit = new Dictionary<Suit, HashSet<int>>();
                foreach (var sequence in groups.Where(g => g.Kind == GroupKind.Sequence))
                {
                    if (!bySuit.TryGetValue(sequence.First.Suit, out var nums))
                    {
                        nums = new HashSet<int>();
                        bySuit[sequence.First.Suit] = nums;
                    }

                    foreach (var tile in sequence.Tiles)
                    {
                        nums.Add(tile.Num);
                    }
                }

                foreach (var nums in bySuit.Values)
                {
                    if (nums.Count >= 9 && Enumerable.Range(1, 9).All(nums.Contains))
                    {
                        return true;
                    }
                }

                return false;
            }

            case "pure_fully_terminal":
            {
                if (groups is null || groups.Count == 0)
                {
                    return false;
                }

                foreach (var group in groups)
                {
                    foreach (var tile in group.Tiles)
                    {
                        if (tile.IsHonor)
                        {
                            return false;
                        }
                    }
                }

                foreach (var group in groups)
                {
                    bool hasTerminal = group.Tiles.Any(t => t.Num == 1 || t.Num == 9);
                    if (!hasTerminal)
                    {
                        return false;
                    }
                }

                return true;
            }

            case "mixed_fully_terminal":
            {
                if (groups is null || groups.Count == 0)
                {
                    return false;
                }

                bool anyHonor = groups.Any(g => g.Tiles.Any(t => t.IsHonor));
                if (!anyHonor)
                {
                    return false;
                }

                foreach (var group in groups)
                {
                    bool ok = group.Tiles.Any(t => t.IsHonor || t.Num == 1 || t.Num == 9);
                    if (!ok)
                    {
                        return false;
                    }
                }

                return true;
            }

            case "small_three_dragons":
            {
                var counts = TileCounts.Create(hand);
                int pung = 0;
                int pair = 0;
                for (int num = 5; num <= 7; num++)
                {
                    int count = counts[TileCounts.Index(Suit.Honor, num)];
                    if (count >= 3)
                    {
                        pung++;
                    }
                    else if (count == 2)
                    {
                        pair++;
                    }
                }

                return pung == 2 && pair == 1;
            }

            case "missing_seven":
            {
                bool hasNumber = false;
                foreach (var tile in hand)
                {
                    if (tile.IsHonor)
                    {
                        continue;
                    }

                    hasNumber = true;
                    if (tile.Num == 7)
                    {
                        return false;
                    }
                }

                return hasNumber;
            }

            case "kachi_7":
            {
                bool hasNumber = hand.Any(t => !t.IsHonor);
                return hasNumber && _winTile is { IsHonor: false, Num: 7 };
            }

            case "all_number":
                return hand.All(t => !t.IsHonor);

            case "all_number_jin":
                if (hand.Any(t => t.IsHonor))
                {
                    return false;
                }

                return groups is not null && groups.All(g => g.Kind != GroupKind.Pung);

            case "sea_moon":
                return _winCtx?.SeaMoon == true;

            case "river_fish":
                return _winCtx?.RiverFish == true;

            case "last_tile":
                return _winCtx is not null && _winCtx.LastTileVisible >= 4;

            case "hai_dei":
                return _winCtx?.HaiDei == true;

            case "same_color_old_young":
            {
                if (groups is null)
                {
                    return false;
                }

                var bySuit = new Dictionary<Suit, (bool Has123, bool Has789)>();
                foreach (var sequence in groups.Where(g => g.Kind == GroupKind.Sequence))
                {
                    var nums = sequence.Tiles.Select(t => t.Num).OrderBy(n => n).ToArray();
                    bool is123 = nums.Length == 3 && nums[0] == 1 && nums[1] == 2 && nums[2] == 3;
                    bool is789 = nums.Length == 3 && nums[0] == 7 && nums[1] == 8 && nums[2] == 9;
                    var current = bySuit.GetValueOrDefault(sequence.First.Suit);
                    bySuit[sequence.First.Suit] = (current.Has123 || is123, current.Has789 || is789);
                }

                return bySuit.Values.Any(v => v.Has123 && v.Has789);
            }

            case "same_color_double_old_young":
            {
                if (groups is null)
                {
                    return false;
                }

                var bySuit = new Dictionary<Suit, (int Count123, int Count789)>();
                foreach (var sequence in groups.Where(g => g.Kind == GroupKind.Sequence))
                {
                    var nums = sequence.Tiles.Select(t => t.Num).OrderBy(n => n).ToArray();
                    bool is123 = nums.Length == 3 && nums[0] == 1 && nums[1] == 2 && nums[2] == 3;
                    bool is789 = nums.Length == 3 && nums[0] == 7 && nums[1] == 8 && nums[2] == 9;
                    var current = bySuit.GetValueOrDefault(sequence.First.Suit);
                    bySuit[sequence.First.Suit] = (
                        current.Count123 + (is123 ? 1 : 0),
                        current.Count789 + (is789 ? 1 : 0));
                }

                return bySuit.Values.Any(v => v.Count123 >= 2 && v.Count789 >= 2);
            }

            case "two_color_double_old_young":
            {
                if (groups is null)
                {
                    return false;
                }

                var suits123 = new HashSet<Suit>();
                var suits789 = new HashSet<Suit>();
                foreach (var sequence in groups.Where(g => g.Kind == GroupKind.Sequence))
                {
                    var nums = sequence.Tiles.Select(t => t.Num).OrderBy(n => n).ToArray();
                    if (nums.Length == 3 && nums[0] == 1 && nums[1] == 2 && nums[2] == 3)
                    {
                        suits123.Add(sequence.First.Suit);
                    }

                    if (nums.Length == 3 && nums[0] == 7 && nums[1] == 8 && nums[2] == 9)
                    {
                        suits789.Add(sequence.First.Suit);
                    }
                }

                return suits123.Count >= 2 && suits789.Count >= 2;
            }

            case "double_even_ko":
                return HasConsecutiveEvenPungs(groups, 2);

            case "triple_even_ko":
                return HasConsecutiveEvenPungs(groups, 3);

            case "less_than_seven":
                return IsLessThanSeven(hand);

            case "greater_than_seven":
                return IsGreaterThanSeven(hand);

            case "three_kingdoms":
            {
                if (groups is null)
                {
                    return false;
                }

                var pungs = groups.Where(g => g.Kind == GroupKind.Pung).ToList();
                if (pungs.Count != 3)
                {
                    return false;
                }

                var suits = new HashSet<Suit>();
                var nums = new List<int>();
                foreach (var pung in pungs)
                {
                    // JS 的字牌 num 是字符串，ns.includes(2/5/8) 严格比较不会命中字牌。
                    if (pung.First.IsHonor)
                    {
                        return false;
                    }

                    suits.Add(pung.First.Suit);
                    nums.Add(pung.First.Num);
                }

                return suits.Count == 3 && nums.Contains(2) && nums.Contains(5) && nums.Contains(8);
            }

            case "north_south_pass":
                return IsNorthSouthPass(hand, groups);

            case "flower_in_mirror":
                return IsFlowerInMirror(hand);

            case "moon_in_water":
                return IsMoonInWater(hand);

            case "chidaichi":
                return IsChidaichi(hand);

            case "water_drop":
                return _winCtx?.WaterDrop == true;

            case "rinshan_gun":
                return _winCtx?.RinshanGun == true;

            case "rinshan_flower":
                return _winCtx?.RinshanFlower == true;

            case "kan_riichi":
                return _winCtx?.KanRiichi == true;

            case "renhe":
                return _winCtx?.Renhe == true;

            case "all_chi":
                return AllMeldsOfType("sequence");

            case "all_pon":
                return AllMeldsOfType("pung");

            case "pure_moon_in_water":
                return IsPureMoonInWater(hand);

            case "three_seven_flower":
                return IsThreeSevenFlower(hand);

            case "beauty_seven_pairs":
                return IsBeautySevenPairs(hand);

            case "four_minkan":
                return _minkanCount >= 4;

            case "four_kings":
            {
                var counts = TileCounts.Create(hand);
                for (int num = 1; num <= 4; num++)
                {
                    if (counts[TileCounts.Index(Suit.Honor, num)] < 3)
                    {
                        return false;
                    }
                }

                return true;
            }

            case "big_three_dragons":
            {
                var counts = TileCounts.Create(hand);
                for (int num = 5; num <= 7; num++)
                {
                    if (counts[TileCounts.Index(Suit.Honor, num)] < 3)
                    {
                        return false;
                    }
                }

                return true;
            }

            case "four_ankou":
                return ConcealedAnkouCount() >= 4;

            case "all_green":
                return IsAllGreen(hand);

            case "thirteen_orphans":
                return IsKokushi13(hand);

            case "nine_gates":
                return IsNineGates(hand);

            case "all_honors":
                return hand.All(t => t.IsHonor);

            case "all_terminals":
                return hand.All(t => !t.IsHonor && (t.Num == 1 || t.Num == 9));

            case "four_kan_daikan":
                return IsFourKanDaikan();

            case "north_south_self":
                return IsNorthSouthSelf(hand, groups);

            default:
                return false;
        }
    }

    // ── 基础形状 ─────────────────────────────────────────────────

    private static bool IsSevenPairs(IReadOnlyList<Tile> hand)
    {
        if (hand.Count != 14)
        {
            return false;
        }

        var counts = TileCounts.Create(hand);
        int pairs = 0;
        foreach (int count in counts)
        {
            if (count % 2 != 0)
            {
                return false;
            }

            pairs += count / 2;
        }

        return pairs == 7;
    }

    private static bool IsSevenHonors(IReadOnlyList<Tile> hand)
    {
        if (hand.Count != 14 || hand.Any(t => !t.IsHonor) || !IsSevenPairs(hand))
        {
            return false;
        }

        var counts = TileCounts.Create(hand);
        for (int num = 1; num <= 7; num++)
        {
            if (counts[TileCounts.Index(Suit.Honor, num)] != 2)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKokushi13(IReadOnlyList<Tile> hand)
    {
        if (hand.Count != 14)
        {
            return false;
        }

        var counts = TileCounts.Create(hand);
        var need = new[]
        {
            (Suit.Man, 1), (Suit.Man, 9),
            (Suit.Pin, 1), (Suit.Pin, 9),
            (Suit.Sou, 1), (Suit.Sou, 9),
            (Suit.Honor, 1), (Suit.Honor, 2), (Suit.Honor, 3), (Suit.Honor, 4),
            (Suit.Honor, 5), (Suit.Honor, 6), (Suit.Honor, 7),
        };
        int total = 0;
        foreach (var (suit, num) in need)
        {
            int count = counts[TileCounts.Index(suit, num)];
            if (count < 1)
            {
                return false;
            }

            total += count;
        }

        return total == 14;
    }

    private static bool IsHalfFlush(IReadOnlyList<Tile> hand)
    {
        bool hasHonor = false;
        var suits = new HashSet<Suit>();
        foreach (var tile in hand)
        {
            if (tile.IsHonor)
            {
                hasHonor = true;
            }
            else
            {
                suits.Add(tile.Suit);
            }
        }

        return hasHonor && suits.Count == 1;
    }

    private static bool IsFullyFlush(IReadOnlyList<Tile> hand)
    {
        var suits = new HashSet<Suit>();
        foreach (var tile in hand)
        {
            if (tile.IsHonor)
            {
                return false;
            }

            suits.Add(tile.Suit);
        }

        return suits.Count == 1;
    }

    private static bool IsFiveGates(IReadOnlyList<Tile> hand)
    {
        var suits = new HashSet<Suit>();
        bool wind = false;
        bool dragon = false;
        foreach (var tile in hand)
        {
            if (tile.IsHonor)
            {
                if (tile.Num is >= 1 and <= 4)
                {
                    wind = true;
                }
                else
                {
                    dragon = true;
                }
            }
            else
            {
                suits.Add(tile.Suit);
            }
        }

        return suits.Count == 3 && wind && dragon;
    }

    private bool HasKachi7(IReadOnlyList<Tile> hand)
    {
        bool hasNumber = hand.Any(t => !t.IsHonor);
        return hasNumber && _winTile is { IsHonor: false, Num: 7 };
    }

    // ── 暗刻 / 特例 ─────────────────────────────────────────────

    private int ConcealedAnkouCount()
    {
        var counts = TileCounts.Create(_concealedHand);
        int total = counts.Count(c => c >= 3);
        return total + _ankanCount;
    }

    private int ConcealedPungCount(IReadOnlyList<Tile> hand)
    {
        var counts = TileCounts.Create(hand);
        return counts.Count(c => c >= 3);
    }

    /// <summary>
    /// 特例加计（第七章）：只有 ike.json 里带 tokurei.when 的役满才可能命中；每役最多加一次。
    /// </summary>
    private int TokureiBonus(YakuDefinition? yaku, IReadOnlyList<Tile> fullHand, IReadOnlyList<Group>? groups)
    {
        if (yaku?.Tokurei is not { Count: > 0 })
        {
            return 0;
        }

        var counts = TileCounts.Create(fullHand);
        int windPungs = 0;
        for (int num = 1; num <= 4; num++)
        {
            if (counts[TileCounts.Index(Suit.Honor, num)] >= 3)
            {
                windPungs++;
            }
        }

        int kanTotal = _melds.Count(m => m.Tiles.Count == 4);
        Tile? win = _winTile;
        _ = groups;

        foreach (var tokurei in yaku.Tokurei)
        {
            switch (tokurei.When)
            {
                case "four_wind_pungs":
                    if (windPungs >= 4)
                    {
                        return tokurei.Bonus;
                    }

                    break;

                case "four_ankan":
                    if (_ankanCount >= 4)
                    {
                        return tokurei.Bonus;
                    }

                    break;

                case "win_1_5_7":
                    if (win is { IsHonor: false } win157
                        && (win157.Num == 1 || win157.Num == 5 || win157.Num == 7))
                    {
                        return tokurei.Bonus;
                    }

                    break;

                case "three_suit_1_or_9":
                {
                    int ones = 0;
                    int nines = 0;
                    foreach (var suit in new[] { Suit.Man, Suit.Pin, Suit.Sou })
                    {
                        if (counts[TileCounts.Index(suit, 1)] >= 3)
                        {
                            ones++;
                        }

                        if (counts[TileCounts.Index(suit, 9)] >= 3)
                        {
                            nines++;
                        }
                    }

                    if (ones >= 3 || nines >= 3)
                    {
                        return tokurei.Bonus;
                    }

                    break;
                }

                case "four_kan_tanki_tsumo":
                    if (kanTotal >= 4 && _selfDraw)
                    {
                        return tokurei.Bonus;
                    }

                    break;

                case "kokushi_13_wait":
                    if (IsKokushi13Wait(fullHand))
                    {
                        return tokurei.Bonus;
                    }

                    break;

                case "kokushi_1_9_tsumo":
                    if (_selfDraw
                        && win is { IsHonor: false } win19
                        && (win19.Num == 1 || win19.Num == 9))
                    {
                        return tokurei.Bonus;
                    }

                    break;
            }
        }

        return 0;
    }

    /// <summary>国士无双 13 面听：去掉和牌张后，恰好是 13 种幺九/字牌各 1 张。</summary>
    private bool IsKokushi13Wait(IReadOnlyList<Tile> hand)
    {
        if (_winTile is not Tile win)
        {
            return false;
        }

        var arr = hand.ToList();
        int position = arr.FindIndex(t => t.Id == win.Id);
        if (position < 0)
        {
            return false;
        }

        arr.RemoveAt(position);
        if (arr.Count != 13)
        {
            return false;
        }

        var counts = TileCounts.Create(arr);
        int distinct = 0;
        foreach (int count in counts)
        {
            if (count == 0)
            {
                continue;
            }

            distinct++;
            if (count != 1)
            {
                return false;
            }
        }

        return distinct == 13;
    }

    // ── 高役形状 ─────────────────────────────────────────────────

    private static bool IsAllGreen(IReadOnlyList<Tile> hand)
    {
        foreach (var tile in hand)
        {
            if (tile.IsHonor)
            {
                if (tile.Num != 6)
                {
                    return false;
                }

                continue;
            }

            if (tile.Suit != Suit.Sou || tile.Num is not (2 or 3 or 4 or 6 or 8))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNineGates(IReadOnlyList<Tile> hand)
    {
        var suits = new HashSet<Suit>();
        foreach (var tile in hand)
        {
            if (tile.IsHonor)
            {
                return false;
            }

            suits.Add(tile.Suit);
        }

        if (suits.Count != 1)
        {
            return false;
        }

        var suit = suits.First();
        var counts = TileCounts.Create(hand);
        for (int num = 1; num <= 9; num++)
        {
            if (counts[TileCounts.Index(suit, num)] < 1)
            {
                return false;
            }
        }

        return counts[TileCounts.Index(suit, 1)] >= 3
            && counts[TileCounts.Index(suit, 9)] >= 3;
    }

    private bool IsThreeSevenFlower(IReadOnlyList<Tile> hand)
    {
        var concealed = TileCounts.Create(_concealedHand);
        if (concealed[TileCounts.Index(Suit.Man, 7)] < 3
            || concealed[TileCounts.Index(Suit.Pin, 7)] < 3
            || concealed[TileCounts.Index(Suit.Sou, 7)] < 3)
        {
            return false;
        }

        foreach (var meld in _melds)
        {
            foreach (var tile in meld.Tiles)
            {
                if (!tile.IsHonor && tile.Num == 7)
                {
                    return false;
                }
            }
        }

        var rest = hand.Where(t => t.IsHonor || t.Num != 7).ToList();
        if (rest.Count != 5)
        {
            return false;
        }

        foreach (var tile in rest)
        {
            if (tile.IsHonor)
            {
                continue;
            }

            if (tile.Num != 1 && tile.Num != 5 && tile.Num != 9)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsBeautySevenPairs(IReadOnlyList<Tile> hand)
    {
        if (hand.Count != 14 || _ankanCount > 0 || _minkanCount > 0)
        {
            return false;
        }

        var counts = TileCounts.Create(hand);
        for (int num = 5; num <= 7; num++)
        {
            if (counts[TileCounts.Index(Suit.Honor, num)] != 4)
            {
                return false;
            }
        }

        return counts[TileCounts.Index(Suit.Pin, 1)] == 2;
    }

    private bool IsFourKanDaikan()
    {
        int kans = _melds.Count(m => m.Tiles.Count == 4);
        if (kans < 4 || !_selfDraw)
        {
            return false;
        }

        return _concealedHand.Count == 2 && _concealedHand[0].Id == _concealedHand[1].Id;
    }

    // ── 南北通 ───────────────────────────────────────────────────

    private bool IsNorthSouthSelf(IReadOnlyList<Tile> hand, IReadOnlyList<Group>? groups)
    {
        if (!IsNorthSouthPassShape(hand, groups))
        {
            return false;
        }

        bool okWin = _selfDraw
            || (_winTile is { IsHonor: false } win
                && (win.Num == 1 || win.Num == 5 || win.Num == 7 || win.Num == 9));
        if (!okWin)
        {
            return false;
        }

        int windNumber = WindNumber(_winCtx?.SeatWind);
        if (windNumber == 0)
        {
            return false;
        }

        var counts = TileCounts.Create(hand);
        return counts[TileCounts.Index(Suit.Honor, windNumber)] >= 3;
    }

    private bool IsNorthSouthPassShape(IReadOnlyList<Tile> hand, IReadOnlyList<Group>? groups) =>
        TryEvaluate("north_south_pass", hand, groups);

    private static bool IsNorthSouthPass(IReadOnlyList<Tile> hand, IReadOnlyList<Group>? groups)
    {
        if (groups is null)
        {
            return false;
        }

        var sequences = groups.Where(g => g.Kind == GroupKind.Sequence).ToList();
        if (sequences.Count == 0)
        {
            return false;
        }

        var bySuit = new Dictionary<Suit, HashSet<int>>();
        foreach (var sequence in sequences)
        {
            if (!bySuit.TryGetValue(sequence.First.Suit, out var nums))
            {
                nums = new HashSet<int>();
                bySuit[sequence.First.Suit] = nums;
            }

            foreach (var tile in sequence.Tiles)
            {
                nums.Add(tile.Num);
            }
        }

        bool hasDragon = bySuit.Values.Any(nums => nums.Count >= 9 && Enumerable.Range(1, 9).All(nums.Contains));
        if (!hasDragon)
        {
            return false;
        }

        var counts = TileCounts.Create(hand);
        int wind2 = counts[TileCounts.Index(Suit.Honor, 2)];
        int wind4 = counts[TileCounts.Index(Suit.Honor, 4)];
        return (wind4 >= 2 && wind2 >= 2)
            || (wind4 >= 3 && wind2 >= 1)
            || (wind2 >= 3 && wind4 >= 1);
    }

    private static int WindNumber(string? wind) => wind switch
    {
        "east" => 1,
        "south" => 2,
        "west" => 3,
        "north" => 4,
        _ => 0,
    };

    // ── 小于七 / 大于七 ─────────────────────────────────────────

    private static bool IsLessThanSeven(IReadOnlyList<Tile> hand)
    {
        for (int start = 1; start <= 4; start++)
        {
            if (XyShape(hand, new[] { start, start + 1, start + 2 }))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGreaterThanSeven(IReadOnlyList<Tile> hand) =>
        XyShape(hand, new[] { 7, 8, 9 });

    private static bool XyShape(IReadOnlyList<Tile> hand, int[] lowNums)
    {
        if (hand.Count != 14)
        {
            return false;
        }

        int honors = hand.Count(t => t.IsHonor);
        int suited = hand.Count - honors;
        if (honors != 8 || suited != 6)
        {
            return false;
        }

        var honorCounts = new int[10];
        foreach (var tile in hand)
        {
            if (tile.IsHonor)
            {
                honorCounts[tile.Num]++;
            }
        }

        int honorKinds = 0;
        for (int num = 1; num <= 9; num++)
        {
            if (honorCounts[num] > 0)
            {
                honorKinds++;
                if (honorCounts[num] != 2)
                {
                    return false;
                }
            }
        }

        if (honorKinds != 4)
        {
            return false;
        }

        int dragons = 0;
        for (int num = 5; num <= 7; num++)
        {
            if (honorCounts[num] > 0)
            {
                dragons++;
            }
        }

        if (dragons != 3)
        {
            return false;
        }

        var suits = new HashSet<Suit>();
        foreach (var tile in hand)
        {
            if (!tile.IsHonor)
            {
                suits.Add(tile.Suit);
            }
        }

        if (suits.Count != 1)
        {
            return false;
        }

        var suitedCounts = new int[10];
        foreach (var tile in hand)
        {
            if (!tile.IsHonor)
            {
                suitedCounts[tile.Num]++;
            }
        }

        foreach (int num in lowNums)
        {
            if (suitedCounts[num] != 2)
            {
                return false;
            }
        }

        for (int num = 1; num <= 9; num++)
        {
            if (suitedCounts[num] > 0 && Array.IndexOf(lowNums, num) < 0)
            {
                return false;
            }
        }

        return true;
    }

    // ── 双逢 ─────────────────────────────────────────────────────

    private static bool HasConsecutiveEvenPungs(IReadOnlyList<Group>? groups, int k)
    {
        if (groups is null)
        {
            return false;
        }

        var pungs = new List<(Suit Suit, int Num)>();
        foreach (var group in groups)
        {
            if (group.Kind != GroupKind.Pung)
            {
                continue;
            }

            var tile = group.First;
            if (tile.IsHonor || tile.Num % 2 != 0)
            {
                continue;
            }

            pungs.Add((tile.Suit, tile.Num));
        }

        for (int i = 0; i < pungs.Count; i++)
        {
            for (int j = i + 1; j < pungs.Count; j++)
            {
                if (pungs[j].Suit == pungs[i].Suit)
                {
                    continue;
                }

                if (Math.Abs(pungs[j].Num - pungs[i].Num) != 2)
                {
                    continue;
                }

                if (k == 2)
                {
                    return true;
                }

                for (int m = j + 1; m < pungs.Count; m++)
                {
                    if (pungs[m].Suit == pungs[i].Suit || pungs[m].Suit == pungs[j].Suit)
                    {
                        continue;
                    }

                    var nums = new[] { pungs[i].Num, pungs[j].Num, pungs[m].Num };
                    Array.Sort(nums);
                    if (nums[1] - nums[0] == 2 && nums[2] - nums[1] == 2)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // ── 水中月 / 镜中花 ─────────────────────────────────────────

    private static int SameHighGroups(int[] counts, Suit suit)
    {
        for (int num = 4; num <= 9; num++)
        {
            if (counts[TileCounts.Index(suit, num)] > 0)
            {
                return 0;
            }
        }

        int c1 = counts[TileCounts.Index(suit, 1)];
        int c2 = counts[TileCounts.Index(suit, 2)];
        int c3 = counts[TileCounts.Index(suit, 3)];
        if (c1 != c2 || c2 != c3 || c1 % 2 != 0)
        {
            return 0;
        }

        return c1 / 2;
    }

    private static (Dictionary<Suit, int> Groups, int[] Counts) MoonShapeStats(IReadOnlyList<Tile> hand)
    {
        var counts = TileCounts.Create(hand);
        var groups = new Dictionary<Suit, int>();
        foreach (var suit in new[] { Suit.Man, Suit.Pin, Suit.Sou })
        {
            int group = SameHighGroups(counts, suit);
            if (group > 0)
            {
                groups[suit] = group;
            }
        }

        return (groups, counts);
    }

    private bool IsMoonInWater(IReadOnlyList<Tile> hand)
    {
        if (_winTile is not { Suit: Suit.Pin, Num: 1 })
        {
            return false;
        }

        if (hand.Count != 14 || !IsSevenPairs(hand))
        {
            return false;
        }

        var (groups, counts) = MoonShapeStats(hand);
        if (counts[TileCounts.Index(Suit.Pin, 1)] != 2 || groups.Count != 2)
        {
            return false;
        }

        return groups.Values.All(value => value == 1);
    }

    private bool IsPureMoonInWater(IReadOnlyList<Tile> hand)
    {
        if (_winTile is not { Suit: Suit.Pin, Num: 1 })
        {
            return false;
        }

        if (hand.Count != 14 || !IsSevenPairs(hand))
        {
            return false;
        }

        var (groups, counts) = MoonShapeStats(hand);
        if (counts[TileCounts.Index(Suit.Pin, 1)] != 2 || groups.Count != 1)
        {
            return false;
        }

        return groups.Values.Single() == 2;
    }

    private bool IsFlowerInMirror(IReadOnlyList<Tile> hand)
    {
        if (_winTile is not { Suit: Suit.Pin, Num: 5 })
        {
            return false;
        }

        if (hand.Count != 14 || !IsSevenPairs(hand))
        {
            return false;
        }

        var (groups, counts) = MoonShapeStats(hand);
        if (counts[TileCounts.Index(Suit.Pin, 5)] != 2 || groups.Count != 2)
        {
            return false;
        }

        return groups.Values.All(value => value == 1);
    }

    // ── 全带赤 ───────────────────────────────────────────────────

    private static bool IsChidaichi(IReadOnlyList<Tile> hand)
    {
        var counts = TileCounts.Create(hand);
        for (int num = 1; num <= 9; num++)
        {
            if (counts[TileCounts.Index(Suit.Man, num)] != 1)
            {
                return false;
            }
        }

        var redSet = new HashSet<int>
        {
            TileCounts.Index(Suit.Honor, 7),
            TileCounts.Index(Suit.Sou, 1),
            TileCounts.Index(Suit.Sou, 5),
            TileCounts.Index(Suit.Sou, 7),
            TileCounts.Index(Suit.Sou, 9),
            TileCounts.Index(Suit.Pin, 1),
            TileCounts.Index(Suit.Pin, 3),
            TileCounts.Index(Suit.Pin, 5),
            TileCounts.Index(Suit.Pin, 6),
            TileCounts.Index(Suit.Pin, 7),
            TileCounts.Index(Suit.Pin, 9),
        };

        for (int index = 0; index < counts.Length; index++)
        {
            if (counts[index] == 0)
            {
                continue;
            }

            if (index < 9)
            {
                continue; // 万子已逐张校验
            }

            if (!redSet.Contains(index))
            {
                return false;
            }
        }

        int extra = hand.Count(t => t.Suit != Suit.Man);
        return extra == 5;
    }

    // ── 役牌 ─────────────────────────────────────────────────────

    private static readonly string[] WindNames = { string.Empty, "东", "南", "西", "北" };
    private static readonly string[] DragonNames = { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "白", "发", "中" };

    /// <summary>
    /// 风牌役牌：圈风与门风总是分别计算；同一种风时合并为一项「东风东」计 2 赋。
    /// 三元牌刻子各 1 赋。
    /// </summary>
    private static List<YakuResult> GetHonorYaku(
        IReadOnlyList<Tile> hand,
        string? seatWind,
        string? roundWind)
    {
        var result = new List<YakuResult>();
        int round = WindNumber(roundWind);
        int seat = WindNumber(seatWind);
        bool hasRound = round != 0 && IsWindPung(hand, round);
        bool hasSeat = seat != 0 && IsWindPung(hand, seat);

        if (hasRound && hasSeat && round == seat)
        {
            result.Add(new YakuResult
            {
                Name = WindNames[round] + "风" + WindNames[seat],
                Fu = 2,
            });
        }
        else
        {
            if (hasRound)
            {
                result.Add(new YakuResult { Name = WindNames[round] + "役牌", Fu = 1 });
            }

            if (hasSeat)
            {
                result.Add(new YakuResult { Name = WindNames[seat] + "役牌", Fu = 1 });
            }
        }

        for (int num = 5; num <= 7; num++)
        {
            if (IsWindPung(hand, num))
            {
                result.Add(new YakuResult { Name = DragonNames[num] + "役牌", Fu = 1 });
            }
        }

        return result;
    }

    private static bool IsWindPung(IReadOnlyList<Tile> hand, int num) =>
        hand.Count(t => t.IsHonor && t.Num == num) >= 3;

    // ── 全求 ─────────────────────────────────────────────────────

    private bool AllMeldsOfType(string want)
    {
        if (_melds.Count != 4)
        {
            return false;
        }

        foreach (var meld in _melds)
        {
            bool isSequence = meld.Type == "chi";
            bool isPung = meld.Type is "pon" or "minkan" or "ankan" or "kakan";
            if (want == "sequence" && !isSequence)
            {
                return false;
            }

            if (want == "pung" && !isPung)
            {
                return false;
            }
        }

        return true;
    }
}
