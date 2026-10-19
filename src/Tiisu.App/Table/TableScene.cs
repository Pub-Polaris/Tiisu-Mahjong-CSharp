using System;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Table;

/// <summary>
/// 牌桌渲染（4:3 改版）：
///   每家手牌带 + 右手侧副露区（副露后手牌缩回另一段），四家牌河贴着中央板，
///   全部按固定矩形排布，永不重叠。
/// </summary>
public sealed class TableScene
{
    public void Draw(ICanvas c, TableViewModel vm, double time, bool debug = false)
    {
        DrawFelt(c);
        DrawRivers(c, vm);
        DrawCenterPlate(c, vm);
        DrawTopSeat(c, vm);
        DrawLeftSeat(c, vm);
        DrawRightSeat(c, vm);
        DrawOwnSeat(c, vm);

        if (debug)
        {
            DrawDebugRegions(c);
        }
    }

    // ── 桌面 ──
    private static void DrawFelt(ICanvas c)
    {
        // 画布底色纯色（letterbox 同色）
        c.FillRect(new RectF(0, 0, TableLayout.Width, TableLayout.Height), Color.Rgb(20, 30, 24));
        var t = TableLayout.Table;
        c.FillRoundRect(t, 20, Color.Rgb(46, 72, 56));
        c.StrokeRoundRect(t, 20, Color.Rgb(24, 36, 28), 2f);
    }

    // ── 四家牌河 ──
    private static void DrawRivers(ICanvas c, TableViewModel vm)
    {
        for (int seat = 0; seat < 4; seat++)
        {
            var view = Seat(vm, seat);
            if (view.Discards.Count == 0)
            {
                continue;
            }

            var g = TableLayout.River(seat);
            int count = Math.Min(view.Discards.Count, TableLayout.RiverCols * 4);
            int rows = Math.Max(1, (count + TableLayout.RiverCols - 1) / TableLayout.RiverCols);

            bool horizontalRows = Math.Abs(g.dyRow) > 0;
            float band = horizontalRows ? g.Bounds.H : g.Bounds.W;
            float need = rows * (horizontalRows ? Math.Abs(g.dyRow) : Math.Abs(g.dxRow));
            float scale = need > band ? band / need : 1f;

            for (int i = 0; i < count; i++)
            {
                int row = i / TableLayout.RiverCols;
                int col = i % TableLayout.RiverCols;
                float x = g.Bounds.X + col * g.dxCol + row * g.dxRow * scale;
                float y = g.Bounds.Y + col * g.dyCol + row * g.dyRow * scale;

                var slot = Math.Abs(g.angle) == 90f
                    ? new RectF(x, y, TableLayout.RiverTileH, TableLayout.RiverTileW)
                    : new RectF(x, y, TableLayout.RiverTileW, TableLayout.RiverTileH);

                if (i == count - 1)
                {
                    c.FillRoundRect(slot.Inflate(2), 4, Color.Rgb(255, 210, 96).WithAlpha(80));
                }

                DrawRotated(c, view.Discards[i].Id, slot, g.angle);
            }
        }
    }

    // ── 中央牌桌板 ──
    private static void DrawCenterPlate(ICanvas c, TableViewModel vm)
    {
        var p = TableLayout.CenterPlate;
        c.FillRoundRect(p, 16, Color.Rgb(52, 62, 74));
        c.StrokeRoundRect(p, 16, Color.Rgb(22, 28, 34), 2f);

        c.DrawText(vm.RoundLabel, new RectF(p.X, p.CenterY - 44, p.W, 28),
            Color.Rgb(140, 246, 236), new TextStyle(20, Bold: true, Center: true));
        c.DrawText("余 " + vm.WallCount, new RectF(p.X, p.CenterY - 12, p.W, 24),
            Color.Rgb(150, 236, 226), new TextStyle(17, Center: true));
        c.DrawText("本场 " + vm.Honba + " · 供托 " + vm.Kyotaku, new RectF(p.X, p.CenterY + 14, p.W, 20),
            Color.Rgb(180, 214, 208), new TextStyle(13, Center: true));

        DrawPlateScore(c, Seat(vm, 0), p.CenterX, p.Bottom - 24);
        DrawPlateScore(c, Seat(vm, 2), p.CenterX, p.Y + 28);
        DrawPlateScore(c, Seat(vm, 3), p.X + 38, p.CenterY + 62);
        DrawPlateScore(c, Seat(vm, 1), p.Right - 38, p.CenterY + 62);

        var b = WindBadges();
        DrawWindBadge(c, Seat(vm, 0).Wind, b[0]);
        DrawWindBadge(c, Seat(vm, 1).Wind, b[1]);
        DrawWindBadge(c, Seat(vm, 2).Wind, b[2]);
        DrawWindBadge(c, Seat(vm, 3).Wind, b[3]);
    }

    private static RectF[] WindBadges()
    {
        var p = TableLayout.CenterPlate;
        return new[]
        {
            new RectF(p.X + 8, p.Bottom - 36, 28, 28),
            new RectF(p.Right - 36, p.Bottom - 36, 28, 28),
            new RectF(p.Right - 36, p.Y + 8, 28, 28),
            new RectF(p.X + 8, p.Y + 8, 28, 28),
        };
    }

    private static void DrawPlateScore(ICanvas c, SeatView seat, float cx, float cy)
    {
        c.DrawText(seat.Score.ToString("N0"), new RectF(cx - 46, cy - 12, 92, 24),
            seat.IsTurn ? Color.Rgb(255, 226, 140) : Color.Rgb(236, 240, 236),
            new TextStyle(16, Bold: seat.IsTurn, Center: true));
    }

    private static void DrawWindBadge(ICanvas c, string wind, RectF r)
    {
        c.FillRoundRect(r, 6, Color.Rgb(30, 36, 42));
        c.StrokeRoundRect(r, 6, Color.Rgb(120, 132, 140), 1f);
        c.DrawText(wind, r, Color.Rgb(240, 244, 240), new TextStyle(16, Bold: true, Center: true));
    }

    // ── 对家（上）──
    private static void DrawTopSeat(ICanvas c, TableViewModel vm)
    {
        var seat = Seat(vm, 2);
        DrawBacksHorizontal(c, seat, TableLayout.TopHandArea);
        DrawMeldsHorizontal(c, seat, TableLayout.TopMeldArea, angle: 180f);
    }

    // ── 上家（左）──
    private static void DrawLeftSeat(ICanvas c, TableViewModel vm)
    {
        var seat = Seat(vm, 3);
        DrawBacksVertical(c, seat, TableLayout.LeftHandArea);
        DrawMeldsVertical(c, seat, TableLayout.LeftMeldArea, angle: -90f);
    }

    // ── 下家（右）──
    private static void DrawRightSeat(ICanvas c, TableViewModel vm)
    {
        var seat = Seat(vm, 1);
        DrawBacksVertical(c, seat, TableLayout.RightHandArea);
        DrawMeldsVertical(c, seat, TableLayout.RightMeldArea, angle: 90f);
    }

    // ── 自家 ──
    private static void DrawOwnSeat(ICanvas c, TableViewModel vm)
    {
        var seat = Seat(vm, 0);
        var area = TableLayout.HandArea;

        int total = seat.Hand.Count;
        int row1 = Math.Min(total, 13);
        int extra = total - row1;
        float step = TableLayout.HandTileW + TableLayout.HandGap;
        float x = area.X;
        float y = area.Y + (area.H - TableLayout.HandTileH) / 2f;

        for (int i = 0; i < row1; i++)
        {
            c.DrawTile(seat.Hand[i].Id, new RectF(x + i * step, y, TableLayout.HandTileW, TableLayout.HandTileH));
        }

        for (int i = 0; i < extra; i++)
        {
            float dx = x + row1 * step - TableLayout.HandGap + TableLayout.DrawnGap;
            c.DrawTile(seat.Hand[row1 + i].Id, new RectF(dx, y, TableLayout.HandTileW, TableLayout.HandTileH));
        }

        DrawMeldsHorizontal(c, seat, TableLayout.OwnMeldArea, angle: 0f);
    }

    // ── 横向手牌带（牌背）──
    private static void DrawBacksHorizontal(ICanvas c, SeatView seat, RectF area)
    {
        int n = Math.Min(seat.HandCount > 0 ? seat.HandCount : 13, 13);
        float step = TableLayout.BackTileW + TableLayout.BackGap;
        float y = area.Y + (area.H - TableLayout.BackTileH) / 2f;
        for (int i = 0; i < n; i++)
        {
            DrawBack(c, new RectF(area.X + i * step, y, TableLayout.BackTileW, TableLayout.BackTileH));
        }
    }

    // ── 竖排手牌带（牌背）──
    private static void DrawBacksVertical(ICanvas c, SeatView seat, RectF area)
    {
        int n = Math.Min(seat.HandCount > 0 ? seat.HandCount : 13, 13);
        float x = area.X + (area.W - TableLayout.SideBackW) / 2f;
        for (int i = 0; i < n; i++)
        {
            DrawBack(c, new RectF(x, area.Y + i * TableLayout.SideBackStep,
                TableLayout.SideBackW, TableLayout.SideBackH));
        }
    }

    // ── 副露（横排）──
    private static void DrawMeldsHorizontal(ICanvas c, SeatView seat, RectF area, float angle)
    {
        if (seat.Melds.Count == 0)
        {
            return;
        }

        const float w = TableLayout.RiverTileW;
        const float h = TableLayout.RiverTileH;
        const float gap = 2f;
        float x = area.X + 4f;
        float y = area.Y + (area.H - h) / 2f;

        foreach (var meld in seat.Melds)
        {
            foreach (var tile in meld.Tiles)
            {
                if (x + w > area.Right - 2f)
                {
                    return;
                }

                DrawRotated(c, tile.Id, new RectF(x, y, w, h), angle);
                x += w + gap;
            }

            x += 6;
        }
    }

    // ── 副露（竖排）──
    private static void DrawMeldsVertical(ICanvas c, SeatView seat, RectF area, float angle)
    {
        if (seat.Melds.Count == 0)
        {
            return;
        }

        const float w = TableLayout.RiverTileW;
        const float h = TableLayout.RiverTileH;
        const float gap = 2f;
        float x = area.X + (area.W - w) / 2f;
        float y = area.Y + 4f;

        foreach (var meld in seat.Melds)
        {
            foreach (var tile in meld.Tiles)
            {
                if (y + h > area.Bottom - 2f)
                {
                    return;
                }

                DrawRotated(c, tile.Id, new RectF(x, y, w, h), angle);
                y += h + gap;
            }

            y += 6;
        }
    }

    private static void DrawBack(ICanvas c, RectF r)
    {
        c.FillRoundRect(r, 4, Color.Rgb(46, 138, 198));
        c.StrokeRoundRect(r, 4, Color.Rgb(16, 40, 62), 1f);
    }

    /// <summary>绕槽位中心旋转绘制牌面。</summary>
    private static void DrawRotated(ICanvas c, string id, RectF slot, float angle)
    {
        if (Math.Abs(angle) < 0.01f)
        {
            c.DrawTile(id, slot);
            return;
        }

        c.Save();
        c.Translate(slot.CenterX, slot.CenterY);
        c.RotateDegrees(angle);
        c.DrawTile(id, new RectF(-TableLayout.RiverTileW / 2f, -TableLayout.RiverTileH / 2f,
            TableLayout.RiverTileW, TableLayout.RiverTileH));
        c.Restore();
    }

    // ── 调试边界 ──
    private static void DrawDebugRegions(ICanvas c)
    {
        void Box(string label, RectF r)
        {
            if (r.W <= 0 || r.H <= 0)
            {
                return;
            }

            c.StrokeRoundRect(r, 2, Color.Rgb(255, 64, 64).WithAlpha(220), 1.5f);
            c.DrawText(label, new RectF(r.X + 2, r.Y + 2, 70, 16), Color.White, new TextStyle(11, Bold: true, Center: false));
        }

        Box("self", TableLayout.HandArea);
        Box("self-meld", TableLayout.OwnMeldArea);
        Box("top", TableLayout.TopHandArea);
        Box("top-meld", TableLayout.TopMeldArea);
        Box("left", TableLayout.LeftHandArea);
        Box("left-meld", TableLayout.LeftMeldArea);
        Box("right", TableLayout.RightHandArea);
        Box("right-meld", TableLayout.RightMeldArea);
        Box("center", TableLayout.CenterPlate);
        for (int seat = 0; seat < 4; seat++)
        {
            Box("river" + seat, TableLayout.River(seat).Bounds);
        }
    }

    private static SeatView Seat(TableViewModel vm, int index) =>
        index < vm.Seats.Count ? vm.Seats[index] : new SeatView { Index = index };
}
