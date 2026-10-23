using System;
using System.Collections.Generic;
using System.Linq;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Table;

/// <summary>
/// 牌桌布局参数（唯一真源）+ Skia 绘制。
///
/// 布局由 <see cref="YogaTableLayout"/> 用 Yoga 计算，本类只提供：
///   · 牌尺寸 / 间距 / 画布等全部可调参数
///   · 牌河几何（沿该家左手边对齐中央框，行向远离中央框生长）
///   · 各家的旋转角与右手方向
///
/// 四家手牌框恒为 739×68，手牌在框内居中；副露按手牌框边缘 + 该家右手方向摆，
/// 所以副露永远不参与手牌定位，也就不会「推开手牌」。
/// </summary>
public sealed class TableLayoutSpec
{
    // ── 牌尺寸：唯一真源，改这里整桌重排 ──
    public float HandW = 50f;
    public float HandH = 68f;
    public float HandGap = 3f;
    public float DrawnGap = 12f;

    /// <summary>手牌框按几张牌定长。七子是 13 张（摸牌时的第 14 张会临时外溢）。</summary>
    public int HandTiles = 13;

    public float MeldW = 24f;
    public float MeldH = 33f;
    public float MeldGap = 1f;
    public float MeldGroupGap = 6f;
    public float MeldPad = 2f;
    public float HandMeldGap = 10f;

    public float RiverW = 50f;
    public float RiverH = 68f;
    public float RiverGap = 3f;
    public int RiverCols = 6;
    public int RiverRows = 3;

    public float CenterSize = 400f;
    public float EdgePad = 24f;
    public float PlateGap = 14f;
    public float Margin = 16f;

    public float CanvasW = 1200f;
    public float CanvasH = 1200f;

    public float FrameLen => HandTiles * HandW + (HandTiles - 1) * HandGap;
    public float FrameDepth => HandH;
    public float RiverSpan => RiverCols * (RiverW + RiverGap) - RiverGap;
    public float RiverDepth => RiverRows * (RiverH + RiverGap) - RiverGap;

    // ── 布局结果 ──
    public RectF PlateRect { get; set; }
    public RectF[] HandRects { get; } = new RectF[4];

    /// <summary>四家副露区（参考框；副露几何仍由手牌框 + 右手方向推导，绘制不读这里）。</summary>
    public RectF[] MeldRects { get; } = new RectF[4];

    /// <summary>四家牌河外接矩形；<see cref="River"/> 由它反推锚点，可被自定义布局覆盖。</summary>
    public RectF[] RiverRects { get; } = new RectF[4];

    /// <summary>左上角快捷键提示条。</summary>
    public RectF HudRect { get; set; } = new RectF(24, 20, 300, 34);

    /// <summary>动作按钮条锚框：按钮纵向顶边与水平中心取自这里（见 <see cref="Ui.ActionBar.Build"/>）。</summary>
    public RectF ActionBarRect { get; set; }

    /// <summary>结算面板默认外框（编辑器参考框；绘制未自定义时仍按内容动态居中）。</summary>
    public static readonly RectF DefaultSettlementRect = new(436, 46, 748, 660);

    /// <summary>结算左侧缩略图默认外框。</summary>
    public static readonly RectF DefaultThumbRect = new(18, 402, 396, 396);

    /// <summary>结算面板框；与 <see cref="DefaultSettlementRect"/> 相同表示未自定义（自动居中）。</summary>
    public RectF SettlementRect { get; set; } = DefaultSettlementRect;

    /// <summary>结算左侧缩略图框。</summary>
    public RectF ThumbRect { get; set; } = DefaultThumbRect;

    /// <summary>是否有外部（保存的 JSON / 编辑器）覆盖过布局；false 时绘制走原生的动态计算。</summary>
    public bool OverridesActive { get; set; }

    /// <summary>
    /// 区域清单：名称 + 读写访问器。绘制继续读原有字段，编辑器通过 <see cref="LayoutRegion.Set"/> 写回。
    /// 牌河 / 副露 / HUD / 按钮条 / 结算面板 / 缩略图都在这里出现一次。
    /// </summary>
    public IReadOnlyList<LayoutRegion> Regions => _regions ??= BuildRegions();

    private IReadOnlyList<LayoutRegion>? _regions;

    /// <summary>一个命名区域的可读写视图。</summary>
    public readonly record struct LayoutRegion(string Id, string Note, Func<RectF> Get, Action<RectF> Set)
    {
        public RectF Rect => Get();
    }

    private IReadOnlyList<LayoutRegion> BuildRegions()
    {
        var list = new List<LayoutRegion>(20);
        string[] keys = { "self", "right", "top", "left" };
        string[] notes = { "自家", "下家", "对家", "上家" };

        for (int i = 0; i < 4; i++)
        {
            int seat = i;
            list.Add(new($"hand-{keys[i]}", notes[i] + "手牌", () => HandRects[seat],
                r => HandRects[seat] = r));
        }

        // 副露区：参考框。副露仍贴着手牌框的右手侧摆（绘制不改），这里给出可枚举的锚位。
        for (int i = 0; i < 4; i++)
        {
            int seat = i;
            list.Add(new($"meld-{keys[i]}", notes[i] + "副露区", () => MeldRects[seat],
                r => MeldRects[seat] = r));
        }

        for (int i = 0; i < 4; i++)
        {
            int seat = i;
            list.Add(new($"river-{keys[i]}", notes[i] + "牌河", () => RiverRects[seat],
                r => RiverRects[seat] = r));
        }

        list.Add(new("plate", "中央板（牌山）", () => PlateRect, r => PlateRect = r));
        list.Add(new("hud", "HUD 提示条", () => HudRect, r => HudRect = r));
        list.Add(new("action-bar", "动作按钮条", () => ActionBarRect, r => ActionBarRect = r));
        list.Add(new("settlement", "结算面板", () => SettlementRect, r => SettlementRect = r));
        list.Add(new("thumbnail", "结算缩略图", () => ThumbRect, r => ThumbRect = r));

        return list;
    }

    /// <summary>
    /// 牌河几何：沿「该家左手边」对齐中央框边沿，行向远离中央框的方向长。
    /// <para><see cref="RiverGeometry.Origin"/> 是第 0 张牌的锚点（画牌用）；
    /// <see cref="RiverGeometry.Bounds"/> 是整片牌河实际占用的外接矩形（标注 / JSON 用）。
    /// 两者不同：负向步进的座位，锚点在外接矩形的一端。</para>
    /// </summary>
    public RiverGeometry River(int seat)
    {
        var bounds = RiverRects[seat];
        if (bounds.W > 0f && bounds.H > 0f)
        {
            return RiverFromBounds(seat, bounds);
        }

        return RiverFromPlate(seat);
    }

    /// <summary>由可编辑的外接矩形反推牌河几何（锚点随方向落在矩形不同角上）。</summary>
    private RiverGeometry RiverFromBounds(int seat, RectF b)
    {
        float w = RiverW, h = RiverH, gap = RiverGap;
        return seat switch
        {
            0 => new RiverGeometry(b, b, w + gap, 0, 0, h + gap, 0f),
            1 => new RiverGeometry(
                new RectF(b.X, b.Bottom - w, b.W, b.H), b,
                0, -(w + gap), h + gap, 0, -90f),
            2 => new RiverGeometry(
                new RectF(b.Right - w, b.Bottom - h, b.W, b.H), b,
                -(w + gap), 0, 0, -(h + gap), 180f),
            _ => new RiverGeometry(
                new RectF(b.Right - h, b.Y, b.W, b.H), b,
                0, w + gap, -(h + gap), 0, 90f),
        };
    }

    private RiverGeometry RiverFromPlate(int seat)
    {
        float w = RiverW, h = RiverH, gap = RiverGap;
        float span = RiverSpan, depth = RiverDepth;
        float G = PlateGap;

        var p = PlateRect;
        switch (seat)
        {
            case 0: // 自家（下）：列向右，行向下
                return new RiverGeometry(
                    new RectF(p.X, p.Bottom + G, span, depth),
                    new RectF(p.X, p.Bottom + G, span, depth),
                    w + gap, 0, 0, h + gap, 0f);

            case 1: // 下家（右）：列向上，行向右
                return new RiverGeometry(
                    new RectF(p.Right + G, p.Bottom - w, depth, span),
                    new RectF(p.Right + G, p.Bottom - span, depth, span),
                    0, -(w + gap), h + gap, 0, -90f);

            case 2: // 对家（上）：列向左，行向上
                return new RiverGeometry(
                    new RectF(p.Right - w, p.Top - G - h, span, depth),
                    new RectF(p.Right - span, p.Top - G - depth, span, depth),
                    -(w + gap), 0, 0, -(h + gap), 180f);

            default: // 上家（左）：列向下，行向左
                return new RiverGeometry(
                    new RectF(p.X - G - h, p.Top, depth, span),
                    new RectF(p.X - G - depth, p.Top, depth, span),
                    0, w + gap, -(h + gap), 0, 90f);
        }
    }

    public readonly record struct RiverGeometry(
        RectF Origin, RectF Bounds, float dxCol, float dyCol, float dxRow, float dyRow, float angle);

    /// <summary>某家的轴向描述：牌面旋转角跟随该家牌河（副露是平放的，与牌河同向）。</summary>
    public static (bool Horizontal, float Angle, int RightDir) Axis(int seat) => seat switch
    {
        0 => (true, 0f, +1),
        1 => (false, -90f, -1),
        2 => (true, 180f, -1),
        _ => (false, 90f, +1),
    };
}

/// <summary>牌桌渲染：按 Yoga 算出的矩形绘制。</summary>
public sealed class TableSceneSkia
{
    public void Draw(ICanvas c, TableViewModel vm, TableLayoutSpec l, int hoverHand = -1)
    {
        c.FillRect(new RectF(0, 0, l.CanvasW, l.CanvasH), Color.Rgb(20, 30, 24));
        c.FillRoundRect(new RectF(l.Margin, l.Margin, l.CanvasW - l.Margin * 2, l.CanvasH - l.Margin * 2),
            20, Color.Rgb(46, 72, 56));

        DrawCenter(c, vm, l.PlateRect);
        DrawRivers(c, vm, l);

        for (int seat = 0; seat < 4; seat++)
        {
            DrawSeat(c, vm, Seat(vm, seat), seat, l, seat == 0 ? hoverHand : -1);
        }

        DrawAiLevelBadges(c, vm, l);
    }

    /// <summary>AI 档位小字：Simple=简 / Normal=普 / Hard=难 / Shura=修，人类座位不显示。</summary>
    private static readonly string[] AiLevelBadges = { "简", "普", "难", "修" };

    /// <summary>
    /// 在每家 AI 手牌框外侧画一枚半透明小标（座位名 · 档位），贴合座位名牌位置，
    /// 不压手牌与牌河。
    /// </summary>
    private static void DrawAiLevelBadges(ICanvas c, TableViewModel vm, TableLayoutSpec l)
    {
        var levels = vm.AiLevels;
        if (levels.Count == 0)
        {
            return;
        }

        const float badgeW = 62f;
        const float badgeH = 24f;

        for (int seat = 1; seat < 4 && seat < levels.Count; seat++)
        {
            var view = Seat(vm, seat);
            if (view.IsHuman)
            {
                continue;
            }

            string level = AiShort(levels[seat]);
            if (level.Length == 0)
            {
                continue;
            }

            var frame = l.HandRects[seat];
            var box = seat switch
            {
                // 对家（上）：手牌框右端下方。
                2 => new RectF(frame.Right - badgeW, frame.Bottom + 8, badgeW, badgeH),

                // 左右两家：手牌框上端外侧。
                _ => new RectF(frame.X, frame.Top - badgeH - 8, badgeW, badgeH),
            };

            c.FillRoundRect(box, 7, Color.Rgb(0, 0, 0).WithAlpha(150));
            c.StrokeRoundRect(box, 7, Color.Rgb(150, 170, 160).WithAlpha(90), 1f);
            c.DrawText(view.Wind + "·" + level, box,
                Color.Rgb(214, 230, 220), new TextStyle(13, Bold: true, Center: true));
        }
    }

    /// <summary>档位名（Simple / Normal / Hard / Shura）→ 单字缩写；未知返回空。</summary>
    private static string AiShort(string level)
    {
        if (string.IsNullOrEmpty(level))
        {
            return string.Empty;
        }

        return level switch
        {
            "Simple" => AiLevelBadges[0],
            "Normal" => AiLevelBadges[1],
            "Hard" => AiLevelBadges[2],
            "Shura" => AiLevelBadges[3],
            _ => string.Empty,
        };
    }

    /// <summary>
    /// 某家手牌的逐张轴对齐矩形。绘制、命中测试、SVG 导出共用同一份算式。
    ///
    /// 关键：牌沿主轴占的长度恒为 HandW（50），跨轴深度恒为 HandH（68）——
    /// 侧家的牌旋转 90° 后 footprint 变成 68×50，「短边朝向中心」由旋转实现，
    /// 不改变沿主轴的步进。这样 14 张在四家都恰好是 739 = 手牌框长度。
    /// </summary>
    public static RectF[] HandTileRects(TableLayoutSpec l, SeatView seat, int index)
    {
        var frame = l.HandRects[index];
        var (horizontal, _, _) = TableLayoutSpec.Axis(index);
        bool self = index == 0;

        int n = seat.Hand.Count > 0 ? seat.Hand.Count : seat.HandCount;
        if (n <= 0)
        {
            n = 13;
        }

        float along = l.HandW;    // 沿主轴步进
        float depth = l.HandH;    // 跨轴深度

        float handLen = n * along + (n - 1) * l.HandGap;
        float drawnExtra = self && n > l.HandTiles ? l.DrawnGap - l.HandGap : 0f;
        float total = handLen + drawnExtra;

        float frameStart = horizontal ? frame.X : frame.Y;
        float frameLen = horizontal ? frame.W : frame.H;
        float frameCrossStart = horizontal ? frame.Y : frame.X;
        float frameCrossLen = horizontal ? frame.H : frame.W;

        float start = frameStart + (frameLen - total) / 2f;
        float cross = frameCrossStart + (frameCrossLen - depth) / 2f;

        var rects = new RectF[n];
        for (int i = 0; i < n; i++)
        {
            float a = start + i * (along + l.HandGap) + (drawnExtra > 0 && i == n - 1 ? drawnExtra : 0f);
            rects[i] = horizontal
                ? new RectF(a, cross, along, depth)
                : new RectF(cross, a, depth, along);
        }

        return rects;
    }

    /// <summary>
    /// 某家副露的逐张槽位（轴对齐 footprint）。与绘制共用，供 SVG 导出与命中测试使用。
    /// 返回的 RectF 是槽位（已含旋转后的宽高），Id 是该张牌的牌面 id。
    /// </summary>
    public static (RectF Slot, string Id)[] MeldTileSlots(TableLayoutSpec l, SeatView seat, int index)
    {
        var placements = MeldTilePlacements(l, seat, index);
        var slots = new (RectF, string)[placements.Length];
        for (int i = 0; i < placements.Length; i++)
        {
            slots[i] = (placements[i].Slot, placements[i].Meld.Tiles[placements[i].TileIndex].Id);
        }

        return slots;
    }

    /// <summary>副露的一张牌：槽位 + 所属组 + 组内牌位（绘制时要按暗杠规则决定牌面 / 牌背）。</summary>
    public readonly record struct MeldTilePlacement(RectF Slot, MeldView Meld, int TileIndex);

    /// <summary>某家副露的逐张槽位与所属副露组（与 <see cref="MeldTileSlots"/> 同一套几何）。</summary>
    public static MeldTilePlacement[] MeldTilePlacements(TableLayoutSpec l, SeatView seat, int index)
    {
        var melds = (seat.Melds ?? Array.Empty<MeldView>()).Where(m => m.Tiles is { Count: > 0 }).ToArray();
        if (melds.Length == 0)
        {
            return Array.Empty<MeldTilePlacement>();
        }

        var frame = l.HandRects[index];
        var (horizontal, _, rightDir) = TableLayoutSpec.Axis(index);
        bool self = index == 0;

        int n = seat.Hand.Count > 0 ? seat.Hand.Count : seat.HandCount;
        if (n <= 0)
        {
            n = 13;
        }

        var tiles = HandTileRects(l, seat, index);
        float total = n * l.HandW + (n - 1) * l.HandGap
                      + (self && n > l.HandTiles ? l.DrawnGap - l.HandGap : 0f);
        float start = horizontal ? tiles[0].X : tiles[0].Y;

        float meldLen = MeldLength(seat, l);
        float cursor = rightDir > 0
            ? start + total + l.HandMeldGap
            : start - l.HandMeldGap - meldLen;

        var list = new List<MeldTilePlacement>();
        float slotW = horizontal ? l.MeldW : l.MeldH;
        float slotH = horizontal ? l.MeldH : l.MeldW;

        foreach (var m in melds)
        {
            cursor += l.MeldPad;
            for (int t = 0; t < m.Tiles.Count; t++)
            {
                var slot = horizontal
                    ? new RectF(cursor, frame.CenterY - slotH / 2f, slotW, slotH)
                    : new RectF(frame.CenterX - slotW / 2f, cursor, slotW, slotH);

                list.Add(new MeldTilePlacement(slot, m, t));
                cursor += l.MeldW + l.MeldGap;
            }

            cursor -= l.MeldGap;
            cursor += l.MeldPad + l.MeldGroupGap;
        }

        return list.ToArray();
    }

    private static void DrawCenter(ICanvas c, TableViewModel vm, RectF p)
    {
        c.FillRoundRect(p, 16, Color.Rgb(52, 62, 74));
        c.StrokeRoundRect(p, 16, Color.Rgb(22, 28, 34), 2f);

        c.DrawText(vm.RoundLabel, new RectF(p.X, p.CenterY - 62, p.W, 34),
            Color.Rgb(140, 246, 236), new TextStyle(26, Bold: true, Center: true));
        c.DrawText("余 " + vm.WallCount, new RectF(p.X, p.CenterY - 18, p.W, 30),
            Color.Rgb(150, 236, 226), new TextStyle(21, Center: true));
        c.DrawText("本场 " + vm.Honba + " · 场赏 " + vm.Kyotaku, new RectF(p.X, p.CenterY + 20, p.W, 24),
            Color.Rgb(180, 214, 208), new TextStyle(15, Center: true));

        const float s = 38f, m = 14f;
        var winds = new[] { Seat(vm, 0).Wind, Seat(vm, 1).Wind, Seat(vm, 2).Wind, Seat(vm, 3).Wind };
        Wind(c, winds[0], new RectF(p.X + m, p.Bottom - m - s, s, s));
        Wind(c, winds[1], new RectF(p.Right - m - s, p.Bottom - m - s, s, s));
        Wind(c, winds[2], new RectF(p.Right - m - s, p.Y + m, s, s));
        Wind(c, winds[3], new RectF(p.X + m, p.Y + m, s, s));
    }

    private static void Wind(ICanvas c, string w, RectF r)
    {
        c.FillRoundRect(r, 8, Color.Rgb(30, 36, 42));
        c.StrokeRoundRect(r, 8, Color.Rgb(120, 132, 140), 1.5f);
        c.DrawText(w, r, Color.Rgb(240, 244, 240), new TextStyle(21, Bold: true, Center: true));
    }

    private static void DrawRivers(ICanvas c, TableViewModel vm, TableLayoutSpec l)
    {
        for (int seat = 0; seat < 4; seat++)
        {
            var view = Seat(vm, seat);
            if (view.Discards.Count == 0)
            {
                continue;
            }

            var g = l.River(seat);
            int count = Math.Min(view.Discards.Count, l.RiverCols * l.RiverRows);

            for (int i = 0; i < count; i++)
            {
                int row = i / l.RiverCols;
                int col = i % l.RiverCols;
                float x = g.Origin.X + col * g.dxCol + row * g.dxRow;
                float y = g.Origin.Y + col * g.dyCol + row * g.dyRow;

                var slot = Math.Abs(g.angle) == 90f
                    ? new RectF(x, y, l.RiverH, l.RiverW)
                    : new RectF(x, y, l.RiverW, l.RiverH);

                if (i == count - 1)
                {
                    c.FillRoundRect(slot.Inflate(2), 4, Color.Rgb(255, 210, 96).WithAlpha(80));
                }

                Rot(c, view.Discards[i].Id, slot.CenterX, slot.CenterY, g.angle, l.RiverW, l.RiverH);
            }
        }
    }

    /// <summary>和牌后暗杠整组的 30% 黑遮罩（0.3 × 255 ≈ 77）。</summary>
    private const byte AnkanMaskAlpha = 77;

    private static void DrawSeat(ICanvas c, TableViewModel vm, SeatView seat, int index, TableLayoutSpec l, int hover = -1)
    {
        var (horizontal, angle, _) = TableLayoutSpec.Axis(index);
        bool self = index == 0;

        int n = seat.Hand.Count > 0 ? seat.Hand.Count : seat.HandCount;
        if (n <= 0)
        {
            n = 13;
        }

        var tiles = HandTileRects(l, seat, index);

        for (int i = 0; i < tiles.Length; i++)
        {
            var r = tiles[i];
            float lift = i == hover ? 10f : 0f;
            float cx = r.CenterX;
            float cy = r.CenterY - lift;

            if (self && i < seat.Hand.Count)
            {
                // 牌面始终按未旋转的 50×68 画，旋转交给 Transform
                Rot(c, seat.Hand[i].Id, cx, cy, angle, l.HandW, l.HandH);
            }
            else
            {
                Back(c, cx, cy, r.W, r.H);
            }

            if (i == hover)
            {
                c.StrokeRoundRect(new RectF(cx - r.W / 2f, cy - r.H / 2f, r.W, r.H),
                    4, Color.Rgb(255, 228, 120), 2f);
            }
        }

        // 副露：暗杠对局中只翻第 2、4 张；结算态整组翻开并盖 30% 黑遮罩。
        bool afterWin = vm.InSettlement;
        Dictionary<MeldView, RectF>? masked = null;
        foreach (var placement in MeldTilePlacements(l, seat, index))
        {
            var slot = placement.Slot;
            var meld = placement.Meld;
            if (meld.IsFaceUp(placement.TileIndex, afterWin))
            {
                Rot(c, meld.Tiles[placement.TileIndex].Id, slot.CenterX, slot.CenterY, angle, l.MeldW, l.MeldH);
            }
            else
            {
                Back(c, slot.CenterX, slot.CenterY, slot.W, slot.H);
            }

            if (meld.IsAnkan && (afterWin || meld.RevealedAfterWin))
            {
                masked ??= new Dictionary<MeldView, RectF>();
                masked[meld] = masked.TryGetValue(meld, out var acc)
                    ? RectF.FromLTRB(
                        Math.Min(acc.Left, slot.Left),
                        Math.Min(acc.Top, slot.Top),
                        Math.Max(acc.Right, slot.Right),
                        Math.Max(acc.Bottom, slot.Bottom))
                    : slot;
            }
        }

        if (masked is not null)
        {
            foreach (var rect in masked.Values)
            {
                c.FillRoundRect(rect.Inflate(1f), 4, Color.Black.WithAlpha(AnkanMaskAlpha));
            }
        }
    }
    private static float MeldLength(SeatView seat, TableLayoutSpec l)
    {
        var melds = (seat.Melds ?? Array.Empty<MeldView>()).Where(m => m.Tiles is { Count: > 0 }).ToArray();
        if (melds.Length == 0)
        {
            return 0f;
        }

        float sum = 0f;
        foreach (var m in melds)
        {
            sum += l.MeldPad * 2f + m.Tiles.Count * l.MeldW
                   + (m.Tiles.Count - 1) * l.MeldGap + l.MeldGroupGap;
        }

        return sum - l.MeldGroupGap;
    }

    private static void Rot(ICanvas c, string id, float cx, float cy, float angle, float w, float h)
    {
        c.Save();
        c.Translate(cx, cy);
        if (Math.Abs(angle) > 0.01f)
        {
            c.RotateDegrees(angle);
        }

        c.DrawTile(id, new RectF(-w / 2f, -h / 2f, w, h));
        c.Restore();
    }

    private static void Back(ICanvas c, float cx, float cy, float w, float h)
    {
        var r = new RectF(cx - w / 2f, cy - h / 2f, w, h);
        c.FillRoundRect(r, 4, Color.Rgb(46, 138, 198));
        c.StrokeRoundRect(r, 4, Color.Rgb(16, 40, 62), 1f);
    }

    private static SeatView Seat(TableViewModel vm, int i) =>
        i < vm.Seats.Count ? vm.Seats[i] : new SeatView { Index = i };
}
