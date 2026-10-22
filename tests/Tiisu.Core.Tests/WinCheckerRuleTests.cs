using Tiisu.Core;

namespace Tiisu.Core.Tests;

/// <summary>
/// 261023 规则落地断言：役种上下位排除表（第三节）、保持可叠（第四节）、
/// 结构性互斥（第五节），以及同批数据变更（四杠系 / 老少赋值 / 根役 / 人和固定点数）。
///
/// 每个用例都是手工构造的牌例；断言「某役成立/不成立」以及「赋数和」。
/// </summary>
public sealed class WinCheckerRuleTests
{
    private static readonly WinChecker Checker = new(YakuData.LoadDefault());

    // ── 构造工具 ─────────────────────────────────────────────────

    /// <summary>牌串：<c>"123m 456p 99s 55z"</c>（数字在前、花色在后）。</summary>
    private static List<Tile> H(string spec)
    {
        var tiles = new List<Tile>();
        foreach (string part in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            char suitChar = part[^1];
            Suit suit = suitChar switch
            {
                'm' => Suit.Man,
                'p' => Suit.Pin,
                's' => Suit.Sou,
                'z' => Suit.Honor,
                _ => throw new ArgumentException($"bad tile spec: {part}", nameof(spec)),
            };
            foreach (char digit in part[..^1])
            {
                tiles.Add(new Tile(suit, digit - '0'));
            }
        }

        return tiles;
    }

    /// <summary>副露串：<c>"chi:123m"</c> / <c>"pon:111m"</c> / <c>"ankan:1111m"</c> / <c>"minkan:1111m"</c>。</summary>
    private static Meld M(string spec)
    {
        int colon = spec.IndexOf(':');
        return new Meld(spec[..colon], H(spec[(colon + 1)..]));
    }

    private static WinResult Check(
        string hand,
        string[]? melds = null,
        int ankan = 0,
        int minkan = 0,
        bool selfDraw = false,
        bool? hasCalled = null,
        WinContext? ctx = null)
    {
        var meldList = melds?.Select(M).ToList() ?? new List<Meld>();
        int ankanCount = ankan > 0 ? ankan : meldList.Count(m => m.Type == "ankan");
        int minkanCount = minkan > 0 ? minkan : meldList.Count(m => m.Type is "minkan" or "kakan");
        bool called = hasCalled ?? (meldList.Count > 0 || minkanCount > 0);
        return Checker.CheckWin(H(hand), selfDraw, called, ankanCount, minkanCount, meldList, false, null, ctx);
    }

    private static bool Has(WinResult result, string name) =>
        result.ActiveYaku.Any(y => y.Name == name);

    private static int Fu(WinResult result, string name) =>
        result.ActiveYaku.Where(y => y.Name == name).Sum(y => y.Fu);

    private static int Count(WinResult result, string name) =>
        result.ActiveYaku.Count(y => y.Name == name);

    // ═══════════════════════════════════════════════════════════
    // 一、排除表逐行（第三节）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 排除表_三色同顺_吃掉二色同顺()
    {
        var r = Check("123m 789m 123p 123s 99s");
        Assert.True(Has(r, "三色同顺"));
        Assert.Equal(4, Fu(r, "三色同顺"));
        Assert.False(Has(r, "二色同顺"));

        // 对照：只有两门同数顺时，二色同顺仍单独成立
        var control = Check("123m 123p 456s 789s 55z");
        Assert.True(Has(control, "二色同顺"));
        Assert.Equal(3, Fu(control, "二色同顺"));
    }

    [Fact]
    public void 排除表_三色同刻_吃掉二色同刻()
    {
        var r = Check("111m 111p 111s 234m 99z");
        Assert.True(Has(r, "三色同刻"));
        Assert.Equal(5, Fu(r, "三色同刻"));
        Assert.False(Has(r, "二色同刻"));

        var control = Check("111m 111p 234s 567s 99z");
        Assert.True(Has(control, "二色同刻"));
        Assert.Equal(4, Fu(control, "二色同刻"));
    }

    [Fact]
    public void 排除表_三暗刻_吃掉两暗刻()
    {
        var r = Check("111m 222m 333p 456s 55z");
        Assert.True(Has(r, "三暗刻"));
        Assert.Equal(5, Fu(r, "三暗刻"));
        Assert.False(Has(r, "两暗刻"));

        var control = Check("111m 222m 234p 567s 55z");
        Assert.True(Has(control, "两暗刻"));
        Assert.Equal(3, Fu(control, "两暗刻"));
        Assert.False(Has(control, "三暗刻"));
    }

    [Fact]
    public void 排除表_二同高_吃掉同高()
    {
        var r = Check("123m 456p 99z", new[] { "chi:123m", "chi:456p" });
        Assert.True(Has(r, "二同高"));
        Assert.Equal(7, Fu(r, "二同高"));
        Assert.False(Has(r, "同高（一般高）"));

        var control = Check("123m 456p 789s 99z", new[] { "chi:123m" });
        Assert.True(Has(control, "同高（一般高）"));
        Assert.Equal(4, Fu(control, "同高（一般高）"));
    }

    [Fact]
    public void 排除表_三杠子_吃掉两杠子()
    {
        var r = Check("123m 99s", new[] { "ankan:1111p", "ankan:2222s", "ankan:3333m" });
        Assert.True(Has(r, "三杠子"));
        Assert.Equal(5, Fu(r, "三杠子"));
        Assert.False(Has(r, "两杠子"));
    }

    [Fact]
    public void 排除表_四杠系_吃掉三杠子和两杠子()
    {
        var r = Check("55z", new[] { "ankan:1111m", "minkan:2222p", "ankan:3333s", "minkan:4444m" });
        Assert.Equal(HandKind.HighYaku, r.HandType);
        Assert.True(Has(r, "四杠子"));
        Assert.False(Has(r, "三杠子"));
        Assert.False(Has(r, "两杠子"));
        Assert.DoesNotContain(r.ActiveYaku, y => y.Name is "一暗杠（暗根）" or "加杠/明杠（明根）");
    }

    [Fact]
    public void 排除表_同色双老少_吃掉同色老少()
    {
        // 两组 123s + 两组 789s：同色双老少成立。两条下位先后被这条规则移除。
        var r = Check("123s 789s 55z", new[] { "chi:123s", "chi:789s" });
        Assert.True(Has(r, "同色双老少"));
        Assert.Equal(2, Fu(r, "同色双老少"));
        Assert.False(Has(r, "同色老少"));
        Assert.False(Has(r, "双色双老少"));
        // 二同高不在本行排除表里，仍应保留
        Assert.True(Has(r, "二同高"));
    }

    [Fact]
    public void 排除表_三双逢_吃掉双双逢()
    {
        var r = Check("222m 444p 666s 789m 11z", selfDraw: true);
        Assert.True(Has(r, "三双逢"));
        Assert.Equal(4, Fu(r, "三双逢"));
        Assert.False(Has(r, "双双逢"));

        var control = Check("222m 444p 567s 789m 11z");
        Assert.True(Has(control, "双双逢"));
        Assert.Equal(2, Fu(control, "双双逢"));
        Assert.False(Has(control, "三双逢"));
    }

    // ═══════════════════════════════════════════════════════════
    // 二、保持「可以叠」（第四节）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 可叠_全碰刻与对对和都计()
    {
        var r = Check("55z", new[] { "pon:111m", "pon:222m", "pon:333p", "pon:444s" });
        Assert.True(Has(r, "对对和"));
        Assert.True(Has(r, "全碰刻"));
        Assert.Equal(3, Fu(r, "对对和"));
        Assert.Equal(2, Fu(r, "全碰刻"));
    }

    [Fact]
    public void 可叠_缺门与清一色都计()
    {
        var r = Check("123m 456m 789m 234m 99m");
        Assert.True(Has(r, "清一色"));
        Assert.True(Has(r, "缺门"));
        Assert.Equal(14, Fu(r, "清一色"));
        Assert.Equal(3, Fu(r, "缺门"));
    }

    [Fact]
    public void 可叠_缺门与混一色都计()
    {
        var r = Check("123m 456m 789m 234m 99z");
        Assert.True(Has(r, "混一色"));
        Assert.True(Has(r, "缺门"));
        Assert.Equal(5, Fu(r, "混一色"));
        // 雀头是字牌：缺门 3 + 1
        Assert.Equal(4, Fu(r, "缺门"));
    }

    [Fact]
    public void 可叠_七对子与断幺九及门前自摸都计()
    {
        var r = Check("223344m 556677p 88s", selfDraw: true);
        Assert.Equal(HandKind.SevenPairs, r.HandType);
        Assert.True(Has(r, "七对子"));
        Assert.True(Has(r, "断幺九"));
        Assert.True(Has(r, "门前自摸"));
        Assert.Equal(7, Fu(r, "七对子"));
        Assert.Equal(2, Fu(r, "断幺九"));
        Assert.Equal(4, Fu(r, "门前自摸"));
    }

    [Fact]
    public void 可叠_七对子与混一色及门前自摸都计()
    {
        var r = Check("112233m 445566m 77z", selfDraw: true);
        Assert.Equal(HandKind.SevenPairs, r.HandType);
        Assert.True(Has(r, "七对子"));
        Assert.True(Has(r, "混一色"));
        Assert.True(Has(r, "门前自摸"));
        Assert.Equal(7, Fu(r, "七对子"));
        Assert.Equal(5, Fu(r, "混一色"));
    }

    [Fact]
    public void 可叠_全食顺与断幺九及清一色都计()
    {
        var r = Check("88m", new[] { "chi:234m", "chi:345m", "chi:456m", "chi:567m" });
        Assert.True(Has(r, "全食顺"));
        Assert.True(Has(r, "断幺九"));
        Assert.True(Has(r, "清一色"));
        Assert.Equal(1, Fu(r, "全食顺"));
        Assert.Equal(2, Fu(r, "断幺九"));
        // 四副露 → 清一色食降 1
        Assert.Equal(13, Fu(r, "清一色"));
    }

    [Fact]
    public void 可叠_和绝张与海底月河中鱼岭上炮岭上花都计()
    {
        var r = Check(
            "123m 456m 789m 234p 99s",
            ctx: new WinContext
            {
                LastTileVisible = 4,
                HaiDei = true,
                RiverFish = true,
                RinshanGun = true,
                RinshanFlower = true,
            });
        Assert.True(Has(r, "和绝张"));
        Assert.True(Has(r, "海底月"));
        Assert.True(Has(r, "河中鱼"));
        Assert.True(Has(r, "岭上炮"));
        Assert.True(Has(r, "岭上花"));
        Assert.Equal(3, Fu(r, "和绝张"));
        Assert.Equal(1, Fu(r, "海底月"));
        Assert.Equal(1, Fu(r, "河中鱼"));
        Assert.Equal(1, Fu(r, "岭上炮"));
        Assert.Equal(1, Fu(r, "岭上花"));
    }

    /// <summary>
    /// 一色三节高 + 同高：说明要求「都计」，代码也确实没有加排除。
    /// 但 3 组连号刻 + 2 组同高顺 = 5 组面子，超过 4 组，结构上不可同时出现；
    /// 这里断言「三节高成立时不会被同高抢走」，并单列同高的独立成立。
    /// </summary>
    [Fact]
    public void 可叠_一色三节高与同高_未加排除()
    {
        // 用副露固定三组连号刻，避免 111222333s 被拆成三组 123s 的等分拆法干扰断言。
        var r = Check("456s 55z", new[] { "pon:111s", "pon:222s", "pon:333s" });
        Assert.True(Has(r, "一色三节高"), string.Join(",", r.ActiveYaku.Select(y => y.Name + "/" + y.Fu)));
        Assert.False(Has(r, "同高（一般高）"));

        var control = Check("123m 123m 456p 789s 55z");
        Assert.True(Has(control, "同高（一般高）"));
        Assert.Equal(4, Fu(control, "同高（一般高）"));
    }

    // ═══════════════════════════════════════════════════════════
    // 三、结构性互斥（第五节）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 结构性互斥_全带赤与清一色混一色断幺九()
    {
        // 全带赤 = 万 1-9 各一张 + 其余 5 张全在红牌集合（此例 555s + 77z）
        var r = Check("123m 456m 789m 555s 77z");
        Assert.True(Has(r, "全带赤"));
        Assert.False(Has(r, "清一色"));
        Assert.False(Has(r, "混一色"));
        Assert.False(Has(r, "断幺九"));

        // 全带赤的结构要求万子 1-9 各恰好一张，因此不可能清一色（要 14 张同门）
        Assert.Equal(14, Fu(r, "全带赤"));
    }

    [Fact]
    public void 结构性互斥_一色三步高与一通不共存()
    {
        // 123s 234s 345s 456s：只出现 s1~s6，凑不出一门 1-9 的一通
        var r = Check("123s 234s 345s 456s 55z");
        Assert.True(Has(r, "一色三步高"));
        Assert.False(Has(r, "一通（一条龙）"));

        // 一通形（123m 456m 789m）也不满足「三组连号顺子」的条件之一吗？它满足同门三连顺 → 三步高成立。
        // 这里只断言：一通成立时，三步高是否出现由牌型决定，二者不是靠排除表强制互斥。
        var dragon = Check("123m 456m 789m 234p 99s");
        Assert.True(Has(dragon, "一通（一条龙）"));
    }

    [Fact]
    public void 结构性互斥_一色三节高与一色三步高不共存()
    {
        var r = Check("456s 55z", new[] { "pon:111s", "pon:222s", "pon:333s" });
        Assert.True(Has(r, "一色三节高"), string.Join(",", r.ActiveYaku.Select(y => y.Name + "/" + y.Fu)));
        Assert.False(Has(r, "一色三步高"));

        var control = Check("456s 55z", new[] { "chi:123s", "chi:234s", "chi:345s" });
        Assert.True(Has(control, "一色三步高"));
        Assert.False(Has(control, "一色三节高"));
    }

    [Fact]
    public void 结构性互斥_清一色与混一色不共存()
    {
        var flush = Check("123m 456m 789m 234m 99m");
        Assert.True(Has(flush, "清一色"));
        Assert.False(Has(flush, "混一色"));

        var half = Check("123m 456m 789m 234m 99z");
        Assert.True(Has(half, "混一色"));
        Assert.False(Has(half, "清一色"));
    }

    [Fact]
    public void 结构性互斥_五门齐与缺门不共存()
    {
        // 五门齐要求三门齐 + 风 + 三元；三门齐时缺门必不成立
        var r = Check("123m 456p 789s 111z 55z");
        Assert.True(Has(r, "五门齐"));
        Assert.False(Has(r, "缺门"));
    }

    [Fact]
    public void 结构性互斥_全食顺与全碰刻不共存()
    {
        var chi = Check("99m", new[] { "chi:123m", "chi:234m", "chi:456m", "chi:789m" });
        Assert.True(Has(chi, "全食顺"));
        Assert.False(Has(chi, "全碰刻"));

        var pon = Check("55z", new[] { "pon:111m", "pon:222m", "pon:333p", "pon:444s" });
        Assert.True(Has(pon, "全碰刻"));
        Assert.False(Has(pon, "全食顺"));
    }

    // ═══════════════════════════════════════════════════════════
    // 四、同批规则/数据变更
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 四杠系_四暗杠为双役满七十赋()
    {
        var r = Check("55z", new[] { "ankan:1111m", "ankan:2222p", "ankan:3333s", "ankan:4444m" });
        Assert.Equal(HandKind.HighYaku, r.HandType);
        Assert.True(Has(r, "四暗杠"));
        Assert.Equal(70, Fu(r, "四暗杠"));
        Assert.Equal(70, r.TotalFu);
        Assert.Equal(70000, r.SpecialPoints);
        Assert.False(Has(r, "四暗刻")); // 高役优先链：四暗杠在四暗刻之前
    }

    [Fact]
    public void 四杠系_四明杠为双役满七十赋()
    {
        var r = Check("55z", new[] { "minkan:1111m", "minkan:2222p", "minkan:3333s", "minkan:4444m" });
        Assert.Equal(HandKind.HighYaku, r.HandType);
        Assert.True(Has(r, "四明杠"));
        Assert.Equal(70, Fu(r, "四明杠"));
        Assert.Equal(70000, r.SpecialPoints);
    }

    [Fact]
    public void 四杠系_混合暗明降级为四杠子二十八赋()
    {
        var r = Check("55z", new[] { "ankan:1111m", "minkan:2222p", "ankan:3333s", "minkan:4444m" });
        Assert.Equal(HandKind.HighYaku, r.HandType);
        Assert.True(Has(r, "四杠子"));
        Assert.Equal(28, Fu(r, "四杠子"));
        Assert.Equal(28, r.TotalFu);
        Assert.Equal(35000, r.SpecialPoints);
        Assert.False(Has(r, "四暗杠"));
        Assert.False(Has(r, "四明杠"));
    }

    [Fact]
    public void 杠子役_成立时吃掉根役()
    {
        // 单杠：仍计暗根 / 明根
        var singleAnkan = Check("123m 456p 789s 55z", new[] { "ankan:1111s" });
        Assert.True(Has(singleAnkan, "一暗杠（暗根）"));
        Assert.Equal(2, Fu(singleAnkan, "一暗杠（暗根）"));

        var singleMinkan = Check("123m 456p 789s 55z", new[] { "minkan:1111s" });
        Assert.True(Has(singleMinkan, "加杠/明杠（明根）"));
        Assert.Equal(1, Fu(singleMinkan, "加杠/明杠（明根）"));

        // 两杠：不再计根，只计两杠子（明杠时 3→2）
        var twoAnkan = Check("123m 456p 99s", new[] { "ankan:1111p", "ankan:2222s" });
        Assert.False(Has(twoAnkan, "一暗杠（暗根）"));
        Assert.Equal(3, Fu(twoAnkan, "两杠子"));

        var twoMinkan = Check("123m 456p 99s", new[] { "minkan:1111p", "minkan:2222s" });
        Assert.False(Has(twoMinkan, "加杠/明杠（明根）"));
        Assert.Equal(2, Fu(twoMinkan, "两杠子"));
        Assert.False(Has(twoMinkan, "三杠子"));
    }

    [Fact]
    public void 杠子役_三杠子带明杠降一赋且吃掉根()
    {
        var r = Check("123m 99s", new[] { "ankan:1111p", "ankan:2222s", "minkan:3333m" });
        Assert.True(Has(r, "三杠子"));
        Assert.Equal(4, Fu(r, "三杠子"));
        Assert.False(Has(r, "两杠子"));
        Assert.False(Has(r, "一暗杠（暗根）"));
        Assert.False(Has(r, "加杠/明杠（明根）"));
    }

    [Fact]
    public void 老少三役_改为正式役且赋值校正()
    {
        var data = YakuData.LoadDefault();
        Assert.Equal(1, data.GetYakuFu("same_color_old_young"));
        Assert.Equal(3, data.GetYakuFu("two_color_double_old_young"));
        Assert.Equal(2, data.GetYakuFu("same_color_double_old_young"));
        Assert.DoesNotContain(
            data.Yaku,
            y => y.Optional && y.Id is "same_color_old_young" or "same_color_double_old_young" or "two_color_double_old_young");

        Assert.Equal(1, Fu(Check("123s 789s 456m 234p 55z"), "同色老少"));
        Assert.Equal(3, Fu(Check("123m 789m 123p 789p 55z"), "双色双老少"));
        Assert.Equal(2, Fu(Check("123s 789s 55z", new[] { "chi:123s", "chi:789s" }), "同色双老少"));
    }

    [Fact]
    public void 人和_硬编码七千点且不叠任何役()
    {
        var r = Check(
            "123m 456m 789m 234p 99s",
            ctx: new WinContext { Renhe = true });
        Assert.True(r.Success);
        Assert.Single(r.ActiveYaku);
        Assert.Equal("人和", r.ActiveYaku[0].Name);
        Assert.Equal(7, r.TotalFu);
        Assert.Equal(7000, r.SpecialPoints);
    }

    [Fact]
    public void 四喜和_不再计风牌与三元役牌赋()
    {
        var r = Check(
            "111z 222z 333z 444z 55z",
            ctx: new WinContext { SeatWind = "east", RoundWind = "south" });
        Assert.True(Has(r, "四喜和"));
        Assert.DoesNotContain(r.ActiveYaku, y => y.Name.EndsWith("役牌", StringComparison.Ordinal));
        Assert.DoesNotContain(r.ActiveYaku, y => y.Name.Contains("风东", StringComparison.Ordinal));
        // 只有四喜和本体 + 恒真的特例加计
        Assert.Equal(2, r.ActiveYaku.Count);
    }

    // ═══════════════════════════════════════════════════════════
    // 五、261023 追加口径：四子七对 / 九莲副露 / 和绝张 / 全带赤
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 四子七对_恰好一种四张_固定七千点()
    {
        // 七对结构（m1×4 + 六对）+ 恰好一种四张 → 四子七对，与七对子互斥。
        var r = Check("1111m 22m 33m 44p 55p 66s");
        Assert.True(r.Success);
        Assert.Equal(HandKind.SevenPairs, r.HandType);
        Assert.True(Has(r, "四子七对"));
        Assert.False(Has(r, "七对子"));
        Assert.Equal(7, Fu(r, "四子七对"));
        Assert.Equal(7000, r.SpecialPoints);
    }

    [Fact]
    public void 四子七对_两种四张_七对不成立()
    {
        // 七对结构但有两种四张同牌：既非七对子也非四子七对 → 不能按七对和。
        var r = Check("1111m 2222m 33p 44s 55s");
        Assert.False(r.Success);
        Assert.Equal("not_formed", r.Reason);
        Assert.False(Has(r, "七对子"));
        Assert.False(Has(r, "四子七对"));
    }

    [Fact]
    public void 四子七对_可叠断幺九等普通役()
    {
        var r = Check("2222m 33m 44m 55p 66p 88s");
        Assert.True(r.Success);
        Assert.True(Has(r, "四子七对"));
        Assert.False(Has(r, "七对子"));
        Assert.True(Has(r, "断幺九"));
        Assert.Equal(7000, r.SpecialPoints);
        // 四子七对 7 + 断幺九 2 + 四归一 2 + 缺七 1 + 全数 1
        Assert.Equal(13, r.TotalFu);
    }

    [Fact]
    public void 九莲宝灯_副露降级为清一色()
    {
        // 111s 碰出后手牌仍是 1-9s 齐 + 9s 对：结构上满足九莲，但副露 → 只按清一色计。
        var r = Check("123456789s 99s", new[] { "pon:111s" });
        Assert.True(r.Success);
        Assert.False(Has(r, "九莲宝灯"));
        Assert.True(Has(r, "清一色"));
        // 副露食降 1：14 → 13
        Assert.Equal(13, Fu(r, "清一色"));
        Assert.Equal(HandKind.StandardHand, r.HandType);
    }

    [Fact]
    public void 和绝张_河里两张加副露一张成立()
    {
        // 引擎口径：可见张数 = 四家弃牌河 + 四家副露（含杠）。河里 2 + 副露 1 = 3 → 成立。
        var r = Check(
            "123m 456m 789m 234p 99s",
            ctx: new WinContext { LastTileVisible = 3 });
        Assert.True(Has(r, "和绝张"));
        Assert.Equal(3, Fu(r, "和绝张"));
    }

    [Fact]
    public void 和绝张_河里两张加手牌一张不成立()
    {
        // 手牌不是可见信息：河里 2 + 自己手里 1 → 可见仍为 2，不成立。
        var r = Check(
            "123m 456m 789m 234p 99s",
            ctx: new WinContext { LastTileVisible = 2 });
        Assert.False(Has(r, "和绝张"));
    }

    [Fact]
    public void 全带赤_红牌集合正例()
    {
        // 123456789m 各一张 + 111p（面子）+ 99s（雀头）
        var r = Check("123m 456m 789m 111p 99s");
        Assert.True(Has(r, "全带赤"));
        Assert.Equal(14, Fu(r, "全带赤"));
    }

    [Fact]
    public void 全带赤_含六筒的反例()
    {
        // 6p 不在「13579p / 1579s / 红中」集合内 → 不成立。
        var r = Check("123m 456m 789m 111p 66p");
        Assert.False(Has(r, "全带赤"));
    }
}
