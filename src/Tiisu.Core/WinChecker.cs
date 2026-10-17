namespace Tiisu.Core;

/// <summary>
/// 七子麻将对局判役核心（Web 侧 <c>winchecker.js</c> 的移植）。
///
/// 关键语义与 JS 保持一致：
/// <list type="bullet">
/// <item><see cref="MaxSplit"/> 开启时枚举全部标准形拆法，按 (总赋数, 役种条数, 先出现) 取最优。</item>
/// <item>国士在结构判定之前旁路；七对在标准形之前判定。</item>
/// <item>高役按 ike.json 的 highYaku 数组顺序取第一个成立者（优先权链）。</item>
/// <item>听牌型奖赏不计入 7 赋门槛，门槛判定之后才叠加。</item>
/// </list>
/// </summary>
public sealed partial class WinChecker
{
    private static readonly string[] LessGreaterIds = { "less_than_seven", "greater_than_seven" };
    private static readonly string[] SevenPairExcludedYaku =
    {
        "all_pungs", "two_ankou", "three_ankou", "one_color_triple",
    };

    private readonly YakuData _yakuData;
    private readonly HashSet<string> _optionalEnabled = new(StringComparer.Ordinal);

    private IReadOnlyList<Tile> _concealedHand = Array.Empty<Tile>();
    private IReadOnlyList<Meld> _melds = Array.Empty<Meld>();
    private int _ankanCount;
    private int _minkanCount;
    private bool _selfDraw;
    private Tile? _winTile;
    private WinContext? _winCtx;
    private IReadOnlyList<Group>? _forcedGroups;

    /// <param name="yakuData">役种数据（通常为 <see cref="YakuData.LoadDefault"/>）。</param>
    /// <param name="optionalEnabled">
    /// 启用的可选役 id。传 null（默认）表示启用全部 optional 役 —— 与 golden 验证台口径一致。
    /// </param>
    public WinChecker(YakuData yakuData, IEnumerable<string>? optionalEnabled = null)
    {
        _yakuData = yakuData ?? throw new ArgumentNullException(nameof(yakuData));
        if (optionalEnabled is null)
        {
            foreach (var yaku in yakuData.Yaku)
            {
                if (yaku.Optional)
                {
                    _optionalEnabled.Add(yaku.Id);
                }
            }

            foreach (var yaku in yakuData.HighYaku)
            {
                if (yaku.Optional)
                {
                    _optionalEnabled.Add(yaku.Id);
                }
            }
        }
        else
        {
            foreach (var id in optionalEnabled)
            {
                _optionalEnabled.Add(id);
            }
        }
    }

    /// <summary>高目优先（枚举全部拆法）。默认开启；关闭后与 JS 的旧行为一致（第一个拆法）。</summary>
    public bool MaxSplit { get; set; } = true;

    /// <summary>
    /// 和牌判定。参数顺序按语义分组；<paramref name="firstTurn"/> 与 JS 一样保留但当前判据未使用。
    /// </summary>
    public WinResult CheckWin(
        IReadOnlyList<Tile> hand,
        bool selfDraw = false,
        bool hasCalled = false,
        int ankanCount = 0,
        int minkanCount = 0,
        IReadOnlyList<Meld>? melds = null,
        bool firstTurn = false,
        Tile? winTile = null,
        WinContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(hand);
        _ = firstTurn;
        melds ??= Array.Empty<Meld>();

        if (!MaxSplit)
        {
            return CheckOne(hand, selfDraw, hasCalled, ankanCount, minkanCount, melds, winTile, context);
        }

        int meldCount = melds.Count;
        int need = 4 - meldCount;
        bool special = meldCount == 0 && (IsSevenPairs(hand) || IsKokushi13(hand));
        if (special || need < 0 || hand.Count != need * 3 + 2)
        {
            return CheckOne(hand, selfDraw, hasCalled, ankanCount, minkanCount, melds, winTile, context);
        }

        var all = SplitStandardAll(hand);
        if (all.Count <= 1)
        {
            return CheckOne(hand, selfDraw, hasCalled, ankanCount, minkanCount, melds, winTile, context);
        }

        WinResult? best = null;
        int bestFu = -1;
        int bestCount = -1;
        foreach (var groups in all)
        {
            _forcedGroups = groups;
            WinResult? result;
            try
            {
                result = CheckOne(hand, selfDraw, hasCalled, ankanCount, minkanCount, melds, winTile, context);
            }
            catch
            {
                result = null;
            }

            if (result is { Success: true })
            {
                int fu = result.TotalFu ?? 0;
                int yakuCount = result.ActiveYaku.Count;
                if (fu > bestFu || (fu == bestFu && yakuCount > bestCount))
                {
                    best = result;
                    bestFu = fu;
                    bestCount = yakuCount;
                }
            }
            else if (best is null)
            {
                best = result;
            }
        }

        _forcedGroups = null;
        return best ?? CheckOne(hand, selfDraw, hasCalled, ankanCount, minkanCount, melds, winTile, context);
    }

    private WinResult CheckOne(
        IReadOnlyList<Tile> hand,
        bool selfDraw,
        bool hasCalled,
        int ankanCount,
        int minkanCount,
        IReadOnlyList<Meld> melds,
        Tile? winTile,
        WinContext? context)
    {
        _winTile = winTile;
        _winCtx = context;
        _concealedHand = hand.ToList();
        _ankanCount = ankanCount;
        _minkanCount = minkanCount;
        _melds = melds.ToList();
        _selfDraw = selfDraw;

        var meldTiles = new List<Tile>();
        foreach (var meld in melds)
        {
            meldTiles.AddRange(meld.Tiles);
        }

        int meldCount = melds.Count;
        int kongCount = melds.Count(m => m.Tiles.Count == 4);
        var fullHand = new List<Tile>(hand.Count + meldTiles.Count);
        fullHand.AddRange(hand);
        fullHand.AddRange(meldTiles);

        int expectedTotal = 14 + kongCount;
        if (fullHand.Count != expectedTotal)
        {
            return NotFormed();
        }

        // ── 国士无双：结构判定之前旁路 ──
        if (meldCount == 0 && IsKokushi13(fullHand))
        {
            var thirteenOrphans = FindHighYaku("thirteen_orphans");
            if (thirteenOrphans is not null)
            {
                int tokurei = TokureiBonus(thirteenOrphans, fullHand, null);
                var yaku = new List<YakuResult> { YakuResult.High(thirteenOrphans) };
                if (tokurei > 0)
                {
                    yaku.Add(new YakuResult { Name = "特例加计", Fu = 0, IsBonus = true, PointsBonus = tokurei });
                }

                return new WinResult
                {
                    Success = true,
                    TotalFu = thirteenOrphans.Fu,
                    ActiveYaku = yaku,
                    HandType = HandKind.HighYaku,
                    SevenHonors = false,
                    SpecialPoints = HighPoints(thirteenOrphans),
                };
            }
        }

        // ── 结构判定 ──
        HandKind? handKind = null;
        int baseFu = 0;
        List<Group>? groups = null;
        if (meldCount == 0 && IsSevenPairs(fullHand))
        {
            handKind = HandKind.SevenPairs;
            baseFu = GetYakuFu("seven_pairs");
        }
        else
        {
            int need = 4 - meldCount;
            if (need < 0 || hand.Count != need * 3 + 2)
            {
                return NotFormed();
            }

            List<Group>? formed = null;
            if (_forcedGroups is not null)
            {
                formed = _forcedGroups.ToList();
            }
            else
            {
                var split = SplitStandard(hand);
                if (split.Success)
                {
                    formed = split.Groups;
                }
            }

            int formedGroupCount = formed is null ? -1 : formed.Count(g => g.Kind != GroupKind.Pair);
            if (formed is null || formedGroupCount != need)
            {
                return NotFormed();
            }

            handKind = HandKind.StandardHand;
            groups = new List<Group>(meldCount + formed.Count);
            foreach (var meld in melds)
            {
                groups.Add(meld.ToGroup());
            }

            groups.AddRange(formed);
            baseFu = 0;
        }

        // ── 小于七 / 大于七：形状本质是七对，必须在 seven_pairs 分支之前先判 ──
        foreach (var highId in LessGreaterIds)
        {
            var high = FindHighYaku(highId);
            if (high is null)
            {
                continue;
            }

            if (!TryEvaluate(highId, fullHand, groups))
            {
                continue;
            }

            int tokurei = TokureiBonus(high, fullHand, groups);
            var yaku = new List<YakuResult> { YakuResult.High(high) };
            int totalFu = high.Fu;
            if (IsHalfFlush(fullHand))
            {
                int halfFlush = GetYakuFu("half_flush");
                totalFu += halfFlush;
                yaku.Add(new YakuResult { Name = "混一色", Fu = halfFlush });
            }

            if (tokurei > 0)
            {
                yaku.Add(new YakuResult { Name = "特例加计", Fu = 0, IsBonus = true, PointsBonus = tokurei });
            }

            return new WinResult
            {
                Success = true,
                TotalFu = totalFu,
                ActiveYaku = yaku,
                HandType = HandKind.HighYaku,
                SevenHonors = false,
                SpecialPoints = HighPoints(high),
            };
        }

        bool sevenHonors = handKind == HandKind.SevenPairs && IsSevenHonors(fullHand);

        int totalFuSum = 0;
        var activeYaku = new List<YakuResult>();

        // ── 高得点役：按 ike.json 顺序取第一个成立者 ──
        YakuDefinition? highFound = null;
        foreach (var high in _yakuData.HighYaku)
        {
            if (high.Menzen && hasCalled)
            {
                continue;
            }

            if (high.NotIf is { Count: > 0 })
            {
                bool blocked = false;
                foreach (var blockedId in high.NotIf)
                {
                    if (TryEvaluate(blockedId, fullHand, groups))
                    {
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                {
                    continue;
                }
            }

            if (TryEvaluate(high.Id, fullHand, groups))
            {
                highFound = high;
                break;
            }
        }

        if (highFound is not null)
        {
            totalFuSum = highFound.Fu;
            int tokurei = TokureiBonus(highFound, fullHand, groups);
            activeYaku.Add(YakuResult.High(highFound));
            if (highFound.PointsBonus is not null)
            {
                // 倍满 +7000 点：点数加项只挂在役本体上，这里仅作展示行。
                activeYaku.Add(new YakuResult { Name = "倍满加计", Fu = 0, IsBonus = true });
            }

            if (highFound.FixedPoints is int fixedPoints
                && (!highFound.FixedPointsWhenCalled || hasCalled))
            {
                totalFuSum = 10;
                activeYaku[0].Fu = 10;
                activeYaku[0].FixedPoints = fixedPoints;
            }

            if (tokurei > 0)
            {
                activeYaku.Add(new YakuResult { Name = "特例加计", Fu = 0, IsBonus = true, PointsBonus = tokurei });
            }

            if ((highFound.Id == "less_than_seven" || highFound.Id == "greater_than_seven")
                && IsHalfFlush(fullHand))
            {
                int halfFlush = GetYakuFu("half_flush");
                totalFuSum += halfFlush;
                activeYaku.Add(new YakuResult { Name = "混一色", Fu = halfFlush });
            }
        }
        else
        {
            foreach (var yaku in _yakuData.Yaku)
            {
                if (yaku.Id == "seven_pairs")
                {
                    continue;
                }

                if (handKind == HandKind.SevenPairs
                    && SevenPairExcludedYaku.Contains(yaku.Id, StringComparer.Ordinal))
                {
                    continue;
                }

                if (yaku.Optional && !_optionalEnabled.Contains(yaku.Id))
                {
                    continue;
                }

                if (yaku.Menzen && hasCalled)
                {
                    continue;
                }

                if (!TryEvaluate(yaku.Id, fullHand, groups))
                {
                    continue;
                }

                totalFuSum += yaku.Fu;
                activeYaku.Add(YakuResult.Normal(yaku));
            }

            totalFuSum += baseFu;

            // 七对子：纯正水中月命中时覆盖七对子（该分支只可能出现在高役缺失时，保留判断以对齐 JS）。
            bool coverSevenPairs = activeYaku.Any(y => y.Id == "pure_moon_in_water");
            if (handKind == HandKind.SevenPairs && !coverSevenPairs)
            {
                activeYaku.Add(new YakuResult { Name = "七对子", Fu = baseFu });
            }

            activeYaku.RemoveAll(y => y.Name is "自摸" or "门前自摸" or "门清自摸");

            if (selfDraw)
            {
                if (handKind == HandKind.SevenPairs)
                {
                    totalFuSum += 4;
                    activeYaku.Add(new YakuResult { Name = "门前自摸", Fu = 4 });
                }
                else if (hasCalled)
                {
                    totalFuSum += 2;
                    activeYaku.Add(new YakuResult { Name = "自摸", Fu = 2 });
                }
                else
                {
                    int menzenTsumo = GetYakuFu("menzen_tsumo");
                    totalFuSum += menzenTsumo;
                    activeYaku.Add(new YakuResult { Name = "门前自摸", Fu = menzenTsumo });
                }
            }

            if (selfDraw && HasKachi7(fullHand))
            {
                int bonus = _yakuData.Kachi7SelfBonus;
                totalFuSum += bonus;
                activeYaku.Add(new YakuResult { Name = "卡七自摸加成", Fu = bonus });
            }

            // 役牌：圈风 + 门风 + 三元；大三元/小三元达成时不再单独计三元役牌赋。
            var honorYaku = GetHonorYaku(fullHand, _winCtx?.SeatWind, _winCtx?.RoundWind);
            bool suppressDragonYaku = activeYaku.Any(y => y.Id is "big_three_dragons" or "small_three_dragons");
            foreach (var honor in honorYaku)
            {
                if (suppressDragonYaku
                    && honor.Name is "白役牌" or "发役牌" or "中役牌")
                {
                    continue;
                }

                totalFuSum += honor.Fu;
                activeYaku.Add(honor);
            }

            if (hasCalled || minkanCount > 0)
            {
                foreach (var yaku in activeYaku)
                {
                    if (yaku.Kaishoku)
                    {
                        yaku.Fu = Math.Max(0, yaku.Fu - 1);
                        totalFuSum = Math.Max(0, totalFuSum - 1);
                    }
                }
            }

            var missingSuit = activeYaku.FirstOrDefault(y => y.Name == "缺门");
            if (missingSuit is not null && groups is not null)
            {
                bool hasHonorGroup = groups.Any(g => g.First.IsHonor && g.Kind is GroupKind.Pair or GroupKind.Pung);
                if (hasHonorGroup)
                {
                    missingSuit.Fu += 1;
                    totalFuSum += 1;
                }
            }

            var allNumberJin = activeYaku.FirstOrDefault(y => y.Name == "全数筋和");
            if (allNumberJin is not null && selfDraw && groups is not null
                && handKind != HandKind.SevenPairs)
            {
                var suits = new HashSet<Suit>();
                foreach (var tile in fullHand)
                {
                    if (!tile.IsHonor)
                    {
                        suits.Add(tile.Suit);
                    }
                }

                if (suits.Count == 3)
                {
                    var tsumo = activeYaku.FirstOrDefault(y => y.Name == "自摸");
                    if (tsumo is not null && tsumo.Fu == 2)
                    {
                        tsumo.Fu = 3;
                        totalFuSum += 1;
                    }
                }
            }

            if (ankanCount > 0 || minkanCount > 0)
            {
                int ankanFu = GetYakuFu("ankan");
                if (ankanFu == 0)
                {
                    ankanFu = 2;
                }

                int minkanFu = GetYakuFu("meikan");
                if (minkanFu == 0)
                {
                    minkanFu = 1;
                }

                for (int i = 0; i < ankanCount; i++)
                {
                    totalFuSum += ankanFu;
                    activeYaku.Add(new YakuResult { Name = "一暗杠（暗根）", Fu = ankanFu });
                }

                for (int i = 0; i < minkanCount; i++)
                {
                    totalFuSum += minkanFu;
                    activeYaku.Add(new YakuResult { Name = "加杠/明杠（明根）", Fu = minkanFu });
                }

                int kanTotal = ankanCount + minkanCount;
                if (kanTotal >= 2)
                {
                    int twoKanFu = GetYakuFu("two_kan");
                    if (twoKanFu == 0)
                    {
                        twoKanFu = 3;
                    }

                    if (minkanCount > 0)
                    {
                        twoKanFu = Math.Max(1, twoKanFu - 1);
                    }

                    totalFuSum += twoKanFu;
                    activeYaku.Add(new YakuResult { Name = "两杠子", Fu = twoKanFu });
                }

                if (kanTotal >= 3)
                {
                    int threeKanFu = GetYakuFu("three_kan");
                    if (threeKanFu == 0)
                    {
                        threeKanFu = 5;
                    }

                    if (minkanCount > 0)
                    {
                        threeKanFu = Math.Max(1, threeKanFu - 1);
                    }

                    totalFuSum += threeKanFu;
                    activeYaku.Add(new YakuResult { Name = "三杠子", Fu = threeKanFu });
                }
            }
        }

        // 大七星无视 7 赋起和门槛：它不计分，由 engine 直接结束本局。
        if (!sevenHonors && totalFuSum < _yakuData.BaseFu)
        {
            return new WinResult
            {
                Success = false,
                Reason = "no_yaku",
                TotalFu = totalFuSum,
                ActiveYaku = activeYaku,
                SevenHonors = sevenHonors,
            };
        }

        // ── 听牌型奖赏（5.5）：门槛之后叠加，不计入起和门槛 ──
        var extraYaku = new List<YakuResult>();
        if (groups is not null)
        {
            var wait = WaitFuOf(fullHand, groups, _winTile);
            if (wait is not null)
            {
                totalFuSum += wait.Fu;
                extraYaku.Add(wait);
            }
        }

        int? specialPoints = null;
        var fixedPointsYaku = activeYaku.FirstOrDefault(y => y.FixedPoints is not null);
        var additiveYaku = activeYaku.FirstOrDefault(y => y.Id is "chidaichi" or "water_drop");
        if (fixedPointsYaku is not null)
        {
            specialPoints = fixedPointsYaku.FixedPoints;
        }
        else if (sevenHonors)
        {
            specialPoints = null;
        }
        else if (additiveYaku is not null)
        {
            int otherFu = activeYaku
                .Where(y => y.Id is not ("chidaichi" or "water_drop"))
                .Sum(y => y.Fu);
            specialPoints = 14000 + otherFu * 700;
        }
        else if (highFound is not null)
        {
            int? basePoints = HighPoints(highFound);
            if (basePoints is not null)
            {
                specialPoints = basePoints;
            }
        }

        return new WinResult
        {
            Success = true,
            TotalFu = totalFuSum,
            ActiveYaku = activeYaku.Concat(extraYaku).ToList(),
            HandType = highFound is not null ? HandKind.HighYaku : handKind,
            SevenHonors = sevenHonors,
            SpecialPoints = specialPoints,
        };
    }

    private static WinResult NotFormed() => new() { Success = false, Reason = "not_formed" };

    private YakuDefinition? FindHighYaku(string id)
    {
        foreach (var yaku in _yakuData.HighYaku)
        {
            if (yaku.Id == id)
            {
                return yaku;
            }
        }

        return null;
    }

    private int GetYakuFu(string id) => _yakuData.GetYakuFu(id);

    /// <summary>JS 的 _highPoints：双役满 70000（三七之花荣和 35000），役满 28 赋 35000，其余 null。</summary>
    private int? HighPoints(YakuDefinition? yaku)
    {
        if (yaku is null)
        {
            return null;
        }

        if (yaku.DoubleYakuman)
        {
            if (yaku.SelfDrawDouble && !_selfDraw)
            {
                return 35000;
            }

            return 70000;
        }

        if (yaku.Fu == 28)
        {
            return 35000;
        }

        return null;
    }

    private bool TryEvaluate(string id, IReadOnlyList<Tile> hand, IReadOnlyList<Group>? groups)
    {
        try
        {
            return Evaluate(id, hand, groups);
        }
        catch
        {
            return false;
        }
    }
}
