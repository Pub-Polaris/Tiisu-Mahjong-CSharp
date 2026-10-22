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

    /// <summary>
    /// 役种上下位排除表（北极星 2026-10-23 确认）：上位役成立时，下位役不再计，
    /// 并从总赋里扣掉下位已加的赋。JS 侧 <c>winchecker.js</c> 的 <c>YAKU_EXCLUSIONS</c> 是同一张表。
    /// </summary>
    private static readonly (string Upper, string Lower)[] YakuExclusions =
    {
        ("three_color_straight", "two_color_straight"),          // 三色同顺 ⊃ 二色同顺
        ("three_color_ko", "two_color_ko"),                      // 三色同刻 ⊃ 二色同刻
        ("three_ankou", "two_ankou"),                            // 三暗刻 ⊃ 两暗刻
        ("double_same_high", "same_high"),                       // 二同高 ⊃ 同高
        ("three_kan", "two_kan"),                                // 三杠子 ⊃ 两杠子
        ("four_kan", "three_kan"),                               // 四杠系 ⊃ 三杠子 / 两杠子
        ("four_kan", "two_kan"),
        ("four_minkan", "three_kan"),
        ("four_minkan", "two_kan"),
        ("four_ankan", "three_kan"),
        ("four_ankan", "two_kan"),
        ("same_color_double_old_young", "same_color_old_young"), // 同色双老少 ⊃ 同色老少
        ("same_color_double_old_young", "two_color_double_old_young"),
        ("triple_even_ko", "double_even_ko"),                    // 三双逢 ⊃ 双双逢
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

    /// <summary>当前启用的可选役 id（供宿主同步 UI 的 <c>optionalEnabled</c>，只读）。</summary>
    public IReadOnlyCollection<string> OptionalEnabled => _optionalEnabled;

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

        // 四子七对：七对结构里恰好一种牌是四张。两种及以上四张时「不能按七对和」（见下方普通役分支）。
        // 注意：这里只约束普通七对子役；美人七对 / 纯正水中月 / 镜中花 等役满牌型本身就有
        // 多个四张，必须仍能按七对结构识别，所以不改 IsSevenPairs 的结构判定。
        int sevenPairsFourKindCount = handKind == HandKind.SevenPairs ? CountFourOfAKind(fullHand) : 0;

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
            // 七对结构里出现两种及以上四张同牌：既不是七对子，也不是四子七对 → 该手不能按七对和。
            // （役满/高役牌型在上面的 highFound 分支已经返回，不受此限。）
            if (handKind == HandKind.SevenPairs && sevenPairsFourKindCount >= 2)
            {
                return NotFormed();
            }

            bool fourPairsSeven = handKind == HandKind.SevenPairs && sevenPairsFourKindCount == 1;
            foreach (var yaku in _yakuData.Yaku)
            {
                if (yaku.Id is "seven_pairs" or "four_pairs_seven")
                {
                    continue;
                }

                // 四子七对与普通役可叠（北极星 2026-10-23），因此四子七对分支不套用七对白名单。
                if (handKind == HandKind.SevenPairs
                    && !fourPairsSeven
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

            // 七对子 / 四子七对：纯正水中月命中时覆盖七对子（该分支只可能出现在高役缺失时，保留判断以对齐 JS）。
            bool coverSevenPairs = activeYaku.Any(y => y.Id == "pure_moon_in_water");
            if (handKind == HandKind.SevenPairs && !coverSevenPairs)
            {
                if (fourPairsSeven)
                {
                    // 四子七对：固定 7000 点（fixedPoints），与七对子互斥，其余役照常累加。
                    var fourPairs = FindYaku("four_pairs_seven");
                    int fourPairsFu = fourPairs?.Fu ?? baseFu;
                    totalFuSum += fourPairsFu;
                    activeYaku.Add(fourPairs is null
                        ? new YakuResult
                        {
                            Name = "四子七对",
                            Fu = fourPairsFu,
                            Id = "four_pairs_seven",
                            FixedPoints = 7000,
                        }
                        : YakuResult.Normal(fourPairs));
                }
                else
                {
                    totalFuSum += baseFu;
                    activeYaku.Add(new YakuResult { Name = "七对子", Fu = baseFu });
                }
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
            // 四喜和：自风/场风/三元役牌赋全部不计（北极星 2026-10-23 从三元扩展到风牌）。
            var honorYaku = GetHonorYaku(fullHand, _winCtx?.SeatWind, _winCtx?.RoundWind);
            bool suppressDragonYaku = activeYaku.Any(y => y.Id is "big_three_dragons" or "small_three_dragons");
            bool suppressAllHonorYaku = activeYaku.Any(y => y.Id == "four_kings");
            foreach (var honor in honorYaku)
            {
                if (suppressAllHonorYaku)
                {
                    continue;
                }

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

                int kanTotal = ankanCount + minkanCount;

                // 杠子役（两杠子 / 三杠子 / 四杠系）成立时「吃掉根」：不再另计一暗杠（暗根）/ 明根。
                // 只有恰好单杠时才计根（北极星 2026-10-23）。
                if (kanTotal == 1)
                {
                    if (ankanCount > 0)
                    {
                        totalFuSum += ankanFu;
                        activeYaku.Add(new YakuResult { Name = "一暗杠（暗根）", Fu = ankanFu });
                    }

                    if (minkanCount > 0)
                    {
                        totalFuSum += minkanFu;
                        activeYaku.Add(new YakuResult { Name = "加杠/明杠（明根）", Fu = minkanFu });
                    }
                }

                // 带明杠时两杠子 3→2、三杠子 5→4（只在存在明杠时降 1）。
                if (kanTotal == 2)
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
                    activeYaku.Add(new YakuResult { Name = "两杠子", Fu = twoKanFu, Id = "two_kan" });
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
                    activeYaku.Add(new YakuResult { Name = "三杠子", Fu = threeKanFu, Id = "three_kan" });
                }
            }

            // 上下位排除：上位成立时扣掉已计入的下位赋（数据驱动，见 YakuExclusions）
            totalFuSum = ApplyYakuExclusions(activeYaku, totalFuSum);
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

            // 四子七对是唯一「固定点数 + 可叠其他役」的役（北极星 2026-10-23）：
            // 点数走 fixedPoints（7000），役表与总赋保留叠役，不做收敛。
            if (fixedPointsYaku.Id != "four_pairs_seven")
            {
                // 其余固定点数役（人和 7000 / 三国将副露 7000 等）：硬编码点数，不叠任何其他役
                // （北极星 2026-10-23）。totalFu / 役表都收敛到这一条，面板不再显示被叠的役。
                totalFuSum = fixedPointsYaku.Fu;
                activeYaku = new List<YakuResult> { fixedPointsYaku };
                extraYaku.Clear();
            }
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

    private YakuDefinition? FindYaku(string id)
    {
        foreach (var yaku in _yakuData.Yaku)
        {
            if (yaku.Id == id)
            {
                return yaku;
            }
        }

        return null;
    }

    /// <summary>七对结构里「四张同牌」的种数（0~3）。</summary>
    private static int CountFourOfAKind(IReadOnlyList<Tile> hand)
    {
        var counts = TileCounts.Create(hand);
        int fourKind = 0;
        foreach (int count in counts)
        {
            if (count >= 4)
            {
                fourKind++;
            }
        }

        return fourKind;
    }

    private int GetYakuFu(string id) => _yakuData.GetYakuFu(id);

    /// <summary>
    /// 应用上下位排除表：上位成立时移除下位，并从总赋里扣掉下位已加的赋。
    /// 就地修改 <paramref name="activeYaku"/>，返回修正后的总赋。
    /// </summary>
    private static int ApplyYakuExclusions(List<YakuResult> activeYaku, int totalFu)
    {
        if (activeYaku.Count == 0)
        {
            return totalFu;
        }

        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var yaku in activeYaku)
        {
            if (yaku.Id is not null)
            {
                present.Add(yaku.Id);
            }
        }

        var remove = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (upper, lower) in YakuExclusions)
        {
            if (present.Contains(upper))
            {
                remove.Add(lower);
            }
        }

        if (remove.Count == 0)
        {
            return totalFu;
        }

        for (int i = activeYaku.Count - 1; i >= 0; i--)
        {
            var yaku = activeYaku[i];
            if (yaku.Id is not null && remove.Contains(yaku.Id))
            {
                totalFu -= yaku.Fu;
                activeYaku.RemoveAt(i);
            }
        }

        return totalFu;
    }

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
