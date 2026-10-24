using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 局末结算覆盖层：深色遮罩 + 金边面板，1200×1200 画布居中。
/// 左边直接画和牌者的手牌（14 张 + 副露），右边只给结论：
/// 和牌者 / 和牌方式 / 役种（名称 + 赋）/ 马赏 / 最终点数 / 四家分数变化。
/// 不显示任何中间计算值（总赋、查表赋、子表点数、庄家 ×1.5、本场加项等）。
/// 多人和牌时用 <see cref="RoundResult.Details"/> 列表翻页（「第 x/N 位」+「下一家」）。
/// 面板高度按内容算，再在画布里垂直居中，不留大片空白。
/// </summary>
public sealed class SettlementOverlay
{
    public const float CanvasW = 1200f;
    public const float CanvasH = 1200f;

    // 外框：宽度固定，高度按内容算；布局编辑器改过 settlement 区域时以自定义矩形为准。
    private const float PanelX = 40f;
    private const float PanelW = 1120f;
    private const float HeaderH = 160f;
    private const float FooterH = 78f;
    private const float MinPanelH = 600f;

    private const float HandX = 64f;
    private const float HandW = 636f;
    private const float InfoX = 728f;
    private const float InfoW = 408f;

    // 手牌：一行 7 张，14 张两行；副露小牌另排。
    private const float TileW = 66f;
    private const float TileH = 90f;
    private const float TileGap = 6f;
    private const float TileRowGap = 12f;
    private const int TilesPerRow = 7;
    private const float MeldTileW = 48f;
    private const float MeldTileH = 64f;
    private const float MeldTileGap = 4f;
    private const float MeldGroupGap = 22f;
    private const float MeldLabelH = 20f;

    private const int MaxYakuRows = 13;

    private static readonly Color Gold = Color.Rgb(214, 180, 104);
    private static readonly Color GoldBright = Color.Rgb(246, 214, 138);
    private static readonly Color TextColor = Color.Rgb(232, 226, 206);
    private static readonly Color Muted = Color.Rgb(150, 164, 152);
    private static readonly Color PanelFill = Color.Rgb(16, 24, 22).WithAlpha(248);
    private static readonly Color ZoneFill = Color.Rgb(11, 17, 15).WithAlpha(210);
    private static readonly Color Hairline = Color.Rgb(70, 88, 78);
    private static readonly Color Positive = Color.Rgb(150, 220, 160);
    private static readonly Color Negative = Color.Rgb(230, 140, 130);

    /// <summary>当前翻到第几位和牌者（多人和牌时 0..N-1）。换了一局的结果对象就归零。</summary>
    private int _index;

    private RoundResult? _synced;

    /// <summary>「下一家 ▶」按钮的屏幕矩形（每次绘制时算，命中测试与绘制共用）。</summary>
    private RectF _nextButton;

    private bool _nextHover;

    /// <summary>和牌者人数（0 = 流局；&gt;1 = 多家和牌）。</summary>
    public static int WinnerCount(RoundResult result) => result.Details?.Count ?? 0;

    /// <summary>惩罚条目数（流局罚项 / 报听错和罚）。</summary>
    public static int PenaltyCount(RoundResult result) => result.Penalties?.Count ?? 0;

    /// <summary>
    /// 总页数：每位和牌者一页（流局算 1 页概况），有惩罚时最后再加一页「惩罚结算」。
    /// </summary>
    public static int PageCount(RoundResult result) =>
        Math.Max(1, WinnerCount(result)) + (PenaltyCount(result) > 0 ? 1 : 0);

    /// <summary>当前是否停在「惩罚结算」页。</summary>
    public bool IsPenaltyPage(RoundResult result)
    {
        Sync(result);
        return PenaltyCount(result) > 0 && _index >= Math.Max(1, WinnerCount(result));
    }

    /// <summary>当前显示第几位（0 基）；结果对象变化时归零。</summary>
    public int CurrentIndex(RoundResult result)
    {
        Sync(result);
        return _index;
    }

    /// <summary>翻页：<paramref name="delta"/> = ±1；只有一页时返回 false（不消费按键）。</summary>
    public bool Step(RoundResult result, int delta)
    {
        Sync(result);
        int count = PageCount(result);
        if (count <= 1)
        {
            return false;
        }

        _index = ((_index + delta) % count + count) % count;
        return true;
    }

    /// <summary>点击「下一个」按钮 → 切到下一页。</summary>
    public bool HitNextButton(float x, float y, RoundResult result) =>
        PageCount(result) > 1 && _nextButton.W > 0f && _nextButton.Contains(x, y);

    /// <summary>上一帧画出的「下一家」按钮矩形（自检 / 命中测试用）。</summary>
    public RectF NextButtonBounds => _nextButton;

    /// <summary>悬停高亮（只在多人和牌时生效）；返回是否落在按钮上。</summary>
    public bool SetPointer(float x, float y, RoundResult result)
    {
        _nextHover = HitNextButton(x, y, result);
        return _nextHover;
    }

    private void Sync(RoundResult result)
    {
        if (!ReferenceEquals(_synced, result))
        {
            _synced = result;
            _index = 0;
            _nextHover = false;
        }

        int count = PageCount(result);
        if (_index >= count)
        {
            _index = count - 1;
        }
    }

    /// <summary>画一屏结算。调用方负责只在本局已结束时调用。</summary>
    public void Draw(ICanvas c, RoundResult result, TableViewModel vm, TableLayoutSpec? layout = null)
    {
        Sync(result);

        bool matchOver = vm.Phase == "match_over" || result.MatchOver;
        var details = result.Details ?? Array.Empty<SettlementDetail>();
        bool flow = details.Count == 0;
        bool penaltyPage = PenaltyCount(result) > 0 && _index >= Math.Max(1, details.Count);
        SettlementDetail? detail = flow || penaltyPage ? null : details[Math.Min(_index, details.Count - 1)];

        // 内容高度 → 面板高度，再在画布里垂直居中。两栏各自按内容定高，板块不被拉长留空。
        float handContent = flow || penaltyPage ? MeasureFlowHands(vm) : MeasureWinnerHand(vm, detail!);
        float infoContent = penaltyPage
            ? MeasurePenaltyInfo(PenaltyCount(result), matchOver)
            : flow
                ? MeasureFlowInfo(matchOver)
                : MeasureWinnerInfo(detail!, matchOver);
        float contentH = Math.Max(handContent, infoContent);

        bool custom = layout is not null && layout.SettlementRect != TableLayoutSpec.DefaultSettlementRect;
        RectF panel;
        if (custom)
        {
            panel = layout!.SettlementRect;
            contentH = Math.Max(contentH, panel.H - HeaderH - FooterH);
        }
        else
        {
            float panelH = Math.Clamp(HeaderH + contentH + 24f + FooterH, MinPanelH, 1104f);
            panel = new RectF(PanelX, (CanvasH - panelH) / 2f, PanelW, panelH);
        }

        // 手牌区：与信息区同高，内容在框内垂直居中；编辑器改过 thumbnail 区域时就按自定义矩形画。
        var handZone = new RectF(HandX, panel.Y + HeaderH, HandW, contentH + 24f);
        if (layout is not null && layout.SettlementHandRect != TableLayoutSpec.DefaultSettlementHandRect)
        {
            handZone = layout.SettlementHandRect;
        }

        var infoZone = new RectF(InfoX, panel.Y + HeaderH, InfoW, contentH + 24f);

        // 全屏遮罩：牌桌透出一点，读作覆盖层而不是另一屏。
        c.FillRect(new RectF(0, 0, CanvasW, CanvasH), Color.Black.WithAlpha(194));

        c.Save();
        c.FillRoundRect(panel, 18, PanelFill);
        c.StrokeRoundRect(panel, 18, Gold, 2f);

        DrawHeader(c, result, details, matchOver, panel);

        DrawZone(c, handZone);
        DrawZone(c, infoZone);

        if (penaltyPage)
        {
            DrawFlowHands(c, vm, handZone);
            DrawPenaltyInfo(c, result, vm, infoZone, matchOver);
        }
        else if (flow)
        {
            DrawFlowHands(c, vm, handZone);
            DrawFlowInfo(c, result, vm, infoZone, matchOver);
        }
        else
        {
            DrawWinnerHand(c, vm, detail!, handZone);
            DrawWinnerInfo(c, result, vm, detail!, infoZone, matchOver);
        }

        DrawFooter(c, details.Count, matchOver, panel);
        c.Restore();
    }

    private static void DrawZone(ICanvas c, RectF zone)
    {
        c.FillRoundRect(zone, 14, ZoneFill);
        c.StrokeRoundRect(zone, 14, Hairline, 1.2f);
    }

    private void DrawHeader(ICanvas c, RoundResult result, IReadOnlyList<SettlementDetail> details,
        bool matchOver, RectF panel)
    {
        float left = panel.X + 32f;
        float right = panel.Right - 32f;

        c.DrawText(matchOver ? "终局" : "本局结算",
            new RectF(left, panel.Y + 14f, 420, 46), Gold,
            new TextStyle(30, Bold: true, Center: false));

        string outcome = details.Count == 0 ? "荒牌流局"
            : details.Count > 1 ? "多家和牌"
            : OutcomeLabel(result);
        string meta = "第 " + result.Round.ToString(CultureInfo.InvariantCulture) + " 局 · " + outcome;
        c.DrawText(meta, new RectF(left, panel.Y + 66f, 560, 28), Muted,
            new TextStyle(16, Center: false));

        _nextButton = default;
        int pages = PageCount(result);
        if (pages > 1)
        {
            var button = new RectF(right - 132f, panel.Y + 18f, 132f, 42f);
            _nextButton = button;

            string page = "第 " + (_index + 1).ToString(CultureInfo.InvariantCulture) + "/" +
                          pages.ToString(CultureInfo.InvariantCulture) + " 页";
            c.DrawText(page, new RectF(button.X - 176f, panel.Y + 26f, 166f, 26f), Muted,
                new TextStyle(15, Center: false, Right: true));

            c.FillRoundRect(button, 10, _nextHover ? Color.Rgb(64, 92, 74) : Color.Rgb(38, 54, 45));
            c.StrokeRoundRect(button, 10, Gold, 1.4f);

            // 「▶」在 Microsoft YaHei 里缺字（渲染成方框），三角改成矢量画。
            c.DrawText("下一个", new RectF(button.X + 6f, button.Y, button.W - 30f, button.H), GoldBright,
                new TextStyle(15, Bold: true, Center: true));
            float tx = button.Right - 30f;
            float ty = button.CenterY;
            c.FillPolygon(new[]
            {
                new CanvasPoint(tx - 5f, ty - 7f),
                new CanvasPoint(tx + 5f, ty),
                new CanvasPoint(tx - 5f, ty + 7f),
            }, GoldBright);
        }

        c.FillRect(new RectF(left, panel.Y + 118f, right - left, 1f), Hairline);
    }

    /// <summary>结算屏底部提示：翻页 / 下一局 / 回菜单。</summary>
    private static void DrawFooter(ICanvas c, int winnerCount, bool matchOver, RectF panel)
    {
        string footer = matchOver
            ? "R / Enter 回菜单"
            : winnerCount > 1
                ? "← / → 切换和牌者 · Enter 下一局 · Esc 回菜单"
                : "Enter 下一局 · Esc 回菜单";

        c.DrawText(footer, new RectF(panel.X + 32f, panel.Bottom - 52f, panel.W - 64f, 28), Muted,
            new TextStyle(15, Center: true));
    }

    // ── 左侧手牌区 ───────────────────────────────────────────────

    private const float HandHeaderH = 74f;

    private static float MeasureWinnerHand(TableViewModel vm, SettlementDetail detail)
    {
        var seat = vm.Seats.FirstOrDefault(s => s.Index == detail.WinnerIndex);
        int handCount = seat?.Hand.Count ?? 0;
        int rows = handCount == 0 ? 0 : (handCount + TilesPerRow - 1) / TilesPerRow;
        float h = HandHeaderH;
        if (rows > 0)
        {
            h += rows * TileH + (rows - 1) * TileRowGap;
        }
        var melds = (seat?.Melds ?? Array.Empty<MeldView>()).Where(m => m.Tiles.Count > 0).ToList();
        int meldRows = PackMeldRows(melds, HandW - 44f);
        if (meldRows > 0)
        {
            h += 22f + meldRows * (MeldLabelH + MeldTileH) + (meldRows - 1) * 18f;
        }

        return h;
    }

    /// <summary>
    /// 和牌者的手牌：14 张（含和了牌）+ 全部副露，一行 7 张、最多两行；
    /// 副露按组分开并标出「吃 / 碰 / 杠 / 暗杠 / 加杠」。
    /// </summary>
    private static void DrawWinnerHand(ICanvas c, TableViewModel vm, SettlementDetail detail, RectF zone)
    {
        var seat = vm.Seats.FirstOrDefault(s => s.Index == detail.WinnerIndex);
        var hand = seat?.Hand ?? Array.Empty<TileView>();
        var melds = (seat?.Melds ?? Array.Empty<MeldView>()).Where(m => m.Tiles.Count > 0).ToList();

        string heading = seat is null ? "和牌者的手牌" : seat.Name + " 的手牌";
        string sub = hand.Count.ToString(CultureInfo.InvariantCulture) + " 张";
        sub += melds.Count == 0 ? " · 无副露" : " · " + melds.Count.ToString(CultureInfo.InvariantCulture) + " 组副露";

        int handRows = hand.Count == 0 ? 0 : (hand.Count + TilesPerRow - 1) / TilesPerRow;

        // 两栏都从框顶 12px 起排，左右标题等高对齐；多余空间留在框底部。
        float y = zone.Y + 12f;
        c.DrawText(heading, new RectF(zone.X + 22, y, zone.W - 44, 26), TextColor,
            new TextStyle(17, Bold: true, Center: false));
        c.DrawText(sub, new RectF(zone.X + 22, y + 30, zone.W - 44, 20), Muted,
            new TextStyle(13, Center: false));
        y += HandHeaderH;

        for (int row = 0; row < handRows; row++)
        {
            int first = row * TilesPerRow;
            int count = Math.Min(TilesPerRow, hand.Count - first);

            // 各行按最长一行对齐（左端一致），末行不居中，避免看着像悬空。
            float widest = Math.Min(TilesPerRow, hand.Count) * TileW +
                           (Math.Min(TilesPerRow, hand.Count) - 1) * TileGap;
            float x = zone.CenterX - widest / 2f;
            float top = y + row * (TileH + TileRowGap);

            for (int i = 0; i < count; i++)
            {
                var tile = hand[first + i];
                var rect = new RectF(x + i * (TileW + TileGap), top, TileW, TileH);
                TableSceneSkia.DrawTileFace(c, tile.Id, rect);

                // 自摸时引擎把摸到的牌留在手里：最后一格标金框（只做视觉提示，不改数据）。
                if (detail.SelfDraw && first + i == hand.Count - 1)
                {
                    c.StrokeRoundRect(rect.Inflate(3f), 8, GoldBright, 2f);
                }
            }
        }

        y += handRows == 0 ? 0f : handRows * TileH + (handRows - 1) * TileRowGap;

        if (melds.Count == 0)
        {
            return;
        }

        y += 22f;

        // 副露：按组横排，放不下就换行；标签写在每组上方。
        float cursorX = zone.X + 22f;
        float rowY = y;
        foreach (var meld in melds)
        {
            float width = meld.Tiles.Count * MeldTileW + (meld.Tiles.Count - 1) * MeldTileGap;
            if (cursorX > zone.X + 22f && cursorX + width > zone.Right - 22f)
            {
                cursorX = zone.X + 22f;
                rowY += MeldLabelH + MeldTileH + 18f;
            }

            c.DrawText(MeldLabel(meld.Type), new RectF(cursorX, rowY, width, MeldLabelH), Muted,
                new TextStyle(13, Center: false));

            for (int i = 0; i < meld.Tiles.Count; i++)
            {
                var rect = new RectF(cursorX + i * (MeldTileW + MeldTileGap), rowY + MeldLabelH,
                    MeldTileW, MeldTileH);
                TableSceneSkia.DrawTileFace(c, meld.Tiles[i].Id, rect, depth: false);

                // 暗杠和牌后才公开：与牌桌一致，整组盖 30% 黑遮罩。
                if (meld.IsAnkan)
                {
                    c.FillRoundRect(rect.Inflate(1f), 4, Color.Black.WithAlpha(77));
                }
            }

            cursorX += width + MeldGroupGap;
        }
    }

    /// <summary>副露组按可用宽度能排几行（与绘制时的换行规则一致）。</summary>
    private static int PackMeldRows(List<MeldView> melds, float maxWidth)
    {
        if (melds.Count == 0)
        {
            return 0;
        }

        int rows = 1;
        float used = 0f;
        bool any = false;
        foreach (var meld in melds)
        {
            float width = meld.Tiles.Count * MeldTileW + (meld.Tiles.Count - 1) * MeldTileGap;
            if (any && used + MeldGroupGap + width > maxWidth)
            {
                rows++;
                used = width;
            }
            else
            {
                used += (any ? MeldGroupGap : 0f) + width;
            }

            any = true;
        }

        return rows;
    }

    private static string MeldLabel(string type) => type switch
    {
        "chi" => "吃",
        "pon" => "碰",
        "minkan" => "明杠",
        "ankan" => "暗杠",
        "kakan" => "加杠",
        _ => "副露",
    };

    private const float FlowTileW = 34f;
    private const float FlowTileH = 46f;
    private const float FlowRowGap = 22f;

    private static float MeasureFlowHands(TableViewModel vm)
    {
        int rows = Math.Min(4, vm.Seats.Count);
        return 30f + rows * FlowTileH + Math.Max(0, rows - 1) * FlowRowGap;
    }

    /// <summary>流局：四家手牌都翻开（不标役），按座位列一行。</summary>
    private static void DrawFlowHands(ICanvas c, TableViewModel vm, RectF zone)
    {
        const float nameW = 84f;
        const float gap = 3f;

        int rows = Math.Min(4, vm.Seats.Count);
        float y = zone.Y + 12f;

        c.DrawText("四家手牌", new RectF(zone.X + 22, y, zone.W - 44, 24), Muted,
            new TextStyle(14, Center: false));
        y += 30f;

        for (int r = 0; r < rows; r++)
        {
            var seat = vm.Seats[r];
            c.DrawText(seat.Name, new RectF(zone.X + 22, y, nameW, FlowTileH), TextColor,
                new TextStyle(14, Center: false));

            int count = Math.Min(seat.Hand.Count, 13);
            for (int i = 0; i < count; i++)
            {
                var rect = new RectF(zone.X + 22 + nameW + i * (FlowTileW + gap), y, FlowTileW, FlowTileH);
                TableSceneSkia.DrawTileFace(c, seat.Hand[i].Id, rect, depth: false);
            }

            // 有人副露时手牌不足 13 张：在牌尾标一下，免得看成漏画。
            if (seat.Melds.Count > 0)
            {
                float endX = zone.X + 22 + nameW + count * (FlowTileW + gap);
                c.DrawText("＋" + seat.Melds.Count.ToString(CultureInfo.InvariantCulture) + " 副露",
                    new RectF(endX + 4, y, 80, FlowTileH), Muted, new TextStyle(12, Center: false));
            }

            y += FlowTileH;
            if (r < rows - 1)
            {
                y += FlowRowGap;
            }
        }
    }

    // ── 右侧信息区 ───────────────────────────────────────────────

    private void DrawWinnerInfo(ICanvas c, RoundResult result, TableViewModel vm, SettlementDetail detail,
        RectF zone, bool matchOver)
    {
        var seat = vm.Seats.FirstOrDefault(s => s.Index == detail.WinnerIndex);
        // 199 号规则：门风随庄轮转（庄家=东），所以显示按门风，而不是固定座次名。
        string who = SeatName(vm, detail.WinnerIndex);
        if (seat is { IsDealer: true })
        {
            who += "（庄）";
        }

        string how = detail.SelfDraw ? "自摸" : "荣和";
        if (!detail.SelfDraw && detail.PayerIndex is int payer)
        {
            how += " · 放铳者 " + SeatName(vm, payer);
        }

        var yaku = detail.Yaku.Where(y => y.Flag != SettlementYakuFlag.Ma).ToList();

        float x = zone.X + 20f;
        float width = zone.W - 40f;
        float right = x + width;
        float y = zone.Y + 12f;

        c.DrawText(who, new RectF(x, y, width, 32), GoldBright, new TextStyle(22, Bold: true, Center: false));
        y += 38f;

        c.DrawText(how, new RectF(x, y, width, 26), TextColor, new TextStyle(16, Center: false));
        y += 30f;

        // 役种：名称 + 赋数。表外赋只给最终赋，不展开 ×700；赐马单独放下面「马赏」。
        c.DrawText("役种", new RectF(x, y, width - 70, 22), Muted, new TextStyle(14, Center: false));
        c.DrawText("赋", new RectF(right - 70, y, 70, 22), Muted,
            new TextStyle(14, Center: false, Right: true));
        y += 24f;

        if (yaku.Count == 0)
        {
            c.DrawText("无役", new RectF(x + 2, y, width - 80, 26), Muted, new TextStyle(16, Center: false));
            y += 28f;
        }

        int rows = Math.Min(yaku.Count, MaxYakuRows);
        for (int i = 0; i < rows; i++)
        {
            var row = yaku[i];
            string label = row.Name + FlagSuffix(row.Flag);
            var color = row.Flag == SettlementYakuFlag.Tableless ? Color.Rgb(206, 190, 150) : TextColor;
            c.DrawText(label, new RectF(x + 2, y, width - 80, 26), color,
                new TextStyle(16, Center: false));
            c.DrawText(row.Fu.ToString(CultureInfo.InvariantCulture),
                new RectF(right - 70, y, 70, 26), color,
                new TextStyle(16, Center: false, Right: true));
            y += 28f;
        }

        if (yaku.Count > rows)
        {
            c.DrawText("… 另有 " + (yaku.Count - rows).ToString(CultureInfo.InvariantCulture) + " 项",
                new RectF(x + 2, y, width, 22), Muted, new TextStyle(13, Center: false));
            y += 24f;
        }

        // 马赏：几匹 + 合计点数（每匹 700）。只给结论，不列到表外赋的算式里。
        if (detail.MaFu > 0)
        {
            y += 12f;
            var box = new RectF(x, y, width, 36f);
            c.FillRoundRect(box, 8, Color.Rgb(44, 62, 50).WithAlpha(220));
            c.StrokeRoundRect(box, 8, Gold.WithAlpha(120), 1f);

            string ma = "马赏 " + detail.MaFu.ToString(CultureInfo.InvariantCulture) + " 匹 · 合计 " +
                        (detail.MaFu * 700).ToString(CultureInfo.InvariantCulture);
            c.DrawText(ma, box, Color.Rgb(232, 218, 176), new TextStyle(15, Center: true));
            y += 44f;
        }

        y += 10f;
        c.FillRect(new RectF(x, y, width, 1f), Hairline);
        y += 14f;

        // 最终点数：这一手实际拿到的点数，唯一的大数字。
        c.DrawText("最终点数", new RectF(x, y, width, 22), Muted, new TextStyle(14, Center: false));
        y += 22f;
        c.DrawText(detail.FinalPoints.ToString(CultureInfo.InvariantCulture),
            new RectF(x, y, width, 60), GoldBright, new TextStyle(44, Bold: true, Center: false));
        y += 60f;

        y += 8f;
        if (matchOver)
        {
            DrawRanking(c, vm, x, width, y);
        }
        else
        {
            DrawScoreChanges(c, result, vm, x, width, y);
        }
    }

    private static float MeasureWinnerInfo(SettlementDetail detail, bool matchOver)
    {
        var yakuCount = detail.Yaku.Count(y => y.Flag != SettlementYakuFlag.Ma);
        float h = 38f + 30f + 24f;                      // 和牌者 / 方式 / 表头
        h += yakuCount == 0 ? 28f : Math.Min(yakuCount, MaxYakuRows) * 28f;
        if (yakuCount > MaxYakuRows)
        {
            h += 24f;
        }

        if (detail.MaFu > 0)
        {
            h += 12f + 44f;
        }

        h += 10f + 14f + 22f + 60f;                     // 分隔线 / 最终点数
        h += 8f + (matchOver ? 26f + 4 * 34f : 26f + 4 * 30f);
        return h;
    }

    /// <summary>流局：只给「荒牌流局」+ 四家分数变化（不扣分），不列役。</summary>
    private void DrawFlowInfo(ICanvas c, RoundResult result, TableViewModel vm, RectF zone, bool matchOver)
    {
        float x = zone.X + 20f;
        float width = zone.W - 40f;
        float y = zone.Y + 12f;

        c.DrawText(DrawReasonText(result.DrawReason), new RectF(x, y, width, 44), GoldBright,
            new TextStyle(28, Bold: true, Center: false));
        y += 48f;

        // 流局也走听牌精算（未听罚符 / 场赏），所以只陈述结果，不写「不扣分」。
        c.DrawText("本局无人和牌", new RectF(x, y, width, 26), TextColor,
            new TextStyle(15, Center: false));
        y += 34f;

        c.FillRect(new RectF(x, y, width, 1f), Hairline);
        y += 18f;

        if (matchOver)
        {
            DrawRanking(c, vm, x, width, y);
            return;
        }

        DrawScoreChanges(c, result, vm, x, width, y);
    }

    /// <summary>第二页：场上实际执行的惩罚结算（未听返杠 / 无役听牌罚 / 报听错和罚）。</summary>
    private void DrawPenaltyInfo(ICanvas c, RoundResult result, TableViewModel vm, RectF zone, bool matchOver)
    {
        float x = zone.X + 20f;
        float width = zone.W - 40f;
        float y = zone.Y + 12f;

        c.DrawText("惩罚结算", new RectF(x, y, width, 44), GoldBright,
            new TextStyle(28, Bold: true, Center: false));
        y += 48f;

        var penalties = result.Penalties ?? Array.Empty<string>();
        foreach (string line in penalties)
        {
            c.DrawText(line, new RectF(x, y, width, 26), Negative, new TextStyle(16, Center: false));
            y += 28f;
        }

        c.FillRect(new RectF(x, y, width, 1f), Hairline);
        y += 18f;
        DrawScoreChanges(c, result, vm, x, width, y);
    }

    private static float MeasurePenaltyInfo(int penaltyCount, bool matchOver)
    {
        float h = 48f + (penaltyCount * 28f) + 18f;
        h += matchOver ? 26f + 4 * 34f : 26f + 4 * 30f;
        return h;
    }

    private static float MeasureFlowInfo(bool matchOver)
    {
        float h = 48f + 34f + 18f;
        h += matchOver ? 26f + 4 * 34f : 26f + 4 * 30f;
        return h;
    }

    /// <summary>四家分数变化：一行「名字 48300 → 59500 +11200」。</summary>
    private static void DrawScoreChanges(ICanvas c, RoundResult result, TableViewModel vm, float x,
        float width, float y)
    {
        float right = x + width;
        c.DrawText("分数变化", new RectF(x, y, width, 24), Muted, new TextStyle(14, Center: false));
        y += 26f;

        for (int i = 0; i < vm.Seats.Count && i < result.Scores.Length; i++)
        {
            var seat = vm.Seats[i];
            int after = result.Scores[i];
            int before = result.ScoresBefore is { } scoresBefore && i < scoresBefore.Count
                ? scoresBefore[i]
                : after;

            c.DrawText(SeatName(vm, seat.Index), new RectF(x, y, 100, 26), TextColor, new TextStyle(16, Center: false));
            c.DrawText(before.ToString(CultureInfo.InvariantCulture) + " → " +
                       after.ToString(CultureInfo.InvariantCulture),
                new RectF(x + 104, y, 200, 26), TextColor, new TextStyle(16, Center: false));

            int delta = after - before;
            string deltaText = delta > 0 ? "+" + delta.ToString(CultureInfo.InvariantCulture)
                : delta < 0 ? delta.ToString(CultureInfo.InvariantCulture)
                : "0";
            var color = delta > 0 ? Positive : delta < 0 ? Negative : Muted;
            c.DrawText(deltaText, new RectF(right - 96, y, 96, 26), color,
                new TextStyle(17, Bold: true, Center: false, Right: true));
            y += 30f;
        }
    }

    private static void DrawRanking(ICanvas c, TableViewModel vm, float x, float width, float y)
    {
        float right = x + width;
        c.DrawText("最终排名", new RectF(x, y, width, 24), Muted, new TextStyle(14, Center: false));
        y += 26f;

        var ranked = vm.Seats.OrderByDescending(s => s.Score).ToList();
        for (int i = 0; i < ranked.Count; i++)
        {
            var seat = ranked[i];
            string name = (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + seat.Name +
                          (seat.IsDealer ? "（庄）" : string.Empty);
            var color = i == 0 ? Gold : TextColor;
            c.DrawText(name, new RectF(x + 4, y, 240, 28), color,
                new TextStyle(i == 0 ? 18 : 17, Bold: i == 0, Center: false));
            c.DrawText(seat.Score.ToString(CultureInfo.InvariantCulture),
                new RectF(right - 150, y, 150, 28), color,
                new TextStyle(i == 0 ? 18 : 17, Bold: i == 0, Center: false, Right: true));
            y += 34f;
        }
    }

    private static string FlagSuffix(SettlementYakuFlag flag) => flag switch
    {
        SettlementYakuFlag.Optional => "（可选）",
        SettlementYakuFlag.Tableless => "（表外）",
        SettlementYakuFlag.FixedPoints => "（固定点数）",
        _ => string.Empty,
    };

    /// <summary>显示名用门风（庄家=东），不用固定座次名。</summary>
    private static string SeatName(TableViewModel vm, int index)
    {
        var seat = vm.Seats.FirstOrDefault(s => s.Index == index);
        if (seat is null)
        {
            return "玩家" + index.ToString(CultureInfo.InvariantCulture);
        }

        string wind = string.IsNullOrEmpty(seat.Wind) ? seat.Name : seat.Wind;
        return seat.IsHuman ? wind + "·你" : wind + "家";
    }

    private static string OutcomeLabel(RoundResult result) => result.Kind switch
    {
        "tsumo" => "自摸",
        "ron" => "荣和",
        "tiisuin" => "大七星",
        "special_flow" => "流局",
        _ => "流局",
    };

    private static string DrawReasonText(string? code) => code switch
    {
        "exhaustive" => "荒牌流局",
        "four_wind_repeat" => "四风连打",
        "nine_orphans" => "九种九牌",
        "thirteen_unrelated" => "十三不靠",
        "triple_ron" => "三家和牌",
        "triple_ron_chankan" => "三家和牌（抢杠）",
        "special" => "特殊流局",
        null or "" => "荒牌流局",
        _ => code,
    };
}
