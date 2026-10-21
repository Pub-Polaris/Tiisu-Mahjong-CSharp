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

    public float MeldW = 24f;
    public float MeldH = 33f;
    public float MeldGap = 1f;
    public float MeldGroupGap = 6f;
    public float MeldPad = 2f;
    public float HandMeldGap = 10f;

    public float RiverW = 60f;
    public float RiverH = 84f;
    public float RiverGap = 4f;
    public int RiverCols = 6;
    public int RiverRows = 3;

    public float CenterSize = 400f;
    public float EdgePad = 24f;
    public float PlateGap = 14f;
    public float Margin = 16f;

    public float CanvasW = 1200f;
    public float CanvasH = 1200f;

    public float FrameLen => 14 * HandW + 13 * HandGap;
    public float FrameDepth => HandH;
    public float RiverSpan => RiverCols * (RiverW + RiverGap) - RiverGap;
    public float RiverDepth => RiverRows * (RiverH + RiverGap) - RiverGap;

    // ── 布局结果 ──
    public RectF PlateRect { get; set; }
    public RectF[] HandRects { get; } = new RectF[4];

    /// <summary>
    /// 牌河几何：沿「该家左手边」对齐中央框边沿，行向远离中央框的方向长。
    /// <para><see cref="RiverGeometry.Origin"/> 是第 0 张牌的锚点（画牌用）；
    /// <see cref="RiverGeometry.Bounds"/> 是整片牌河实际占用的外接矩形（标注 / JSON 用）。
    /// 两者不同：负向步进的座位，锚点在外接矩形的一端。</para>
    /// </summary>
    public RiverGeometry River(int seat)
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
                    0, -(w + gap), h + gap, 0, 90f);

            case 2: // 对家（上）：列向左，行向上
                return new RiverGeometry(
                    new RectF(p.Right - w, p.Top - G - h, span, depth),
                    new RectF(p.Right - span, p.Top - G - depth, span, depth),
                    -(w + gap), 0, 0, -(h + gap), 180f);

            default: // 上家（左）：列向下，行向左
                return new RiverGeometry(
                    new RectF(p.X - G - h, p.Top, depth, span),
                    new RectF(p.X - G - depth, p.Top, depth, span),
                    0, w + gap, -(h + gap), 0, -90f);
        }
    }

    public readonly record struct RiverGeometry(
        RectF Origin, RectF Bounds, float dxCol, float dyCol, float dxRow, float dyRow, float angle);

    /// <summary>某家的轴向描述（与 V3 相同）。</summary>
    public static (bool Horizontal, float Angle, int RightDir) Axis(int seat) => seat switch
    {
        0 => (true, 0f, +1),
        1 => (false, 90f, -1),
        2 => (true, 180f, -1),
        _ => (false, -90f, +1),
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
            DrawSeat(c, Seat(vm, seat), seat, l, seat == 0 ? hoverHand : -1);
        }
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
        float drawnExtra = self && n >= 2 ? l.DrawnGap - l.HandGap : 0f;
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
        var melds = (seat.Melds ?? Array.Empty<MeldView>()).Where(m => m.Tiles is { Count: > 0 }).ToArray();
        if (melds.Length == 0)
        {
            return Array.Empty<(RectF, string)>();
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
                      + (self && n >= 2 ? l.DrawnGap - l.HandGap : 0f);
        float start = horizontal ? tiles[0].X : tiles[0].Y;

        float meldLen = MeldLength(seat, l);
        float cursor = rightDir > 0
            ? start + total + l.HandMeldGap
            : start - l.HandMeldGap - meldLen;

        var list = new List<(RectF, string)>();
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

                list.Add((slot, m.Tiles[t].Id));
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
        c.DrawText("本场 " + vm.Honba + " · 供托 " + vm.Kyotaku, new RectF(p.X, p.CenterY + 20, p.W, 24),
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

    private static void DrawSeat(ICanvas c, SeatView seat, int index, TableLayoutSpec l, int hover = -1)
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

        foreach (var (slot, id) in MeldTileSlots(l, seat, index))
        {
            Rot(c, id, slot.CenterX, slot.CenterY, angle, l.MeldW, l.MeldH);
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
