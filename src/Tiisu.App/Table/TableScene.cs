using System;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Table;

/// <summary>
/// 雀魂式牌桌渲染：中央牌桌板 + 回字形牌河 + 13+1 手牌 + 贴右副露。
/// 只吃 <see cref="TableViewModel"/>；每个区域固定，牌再多也不越界。
/// </summary>
public sealed class TableScene
{
    public void Draw(ICanvas c, TableViewModel vm, double time, bool debug = false)
    {
        DrawFelt(c);
        if (debug)
        {
            DrawDebugRegions(c);
        }

        DrawRivers(c, vm);
        DrawCenterPlate(c, vm);
        DrawTopSeat(c, vm);
        DrawLeftSeat(c, vm);
        DrawRightSeat(c, vm);
        DrawOwnSeat(c, vm);
    }


    /// <summary>调试：把区域框和编号画出来（北极星指出哪个区域不对时用）。</summary>
    private static void DrawDebugRegions(ICanvas c)
    {
        void Box(string label, RectF r)
        {
            if (r.W <= 0 || r.H <= 0)
            {
                return;
            }

            c.StrokeRoundRect(r, 4, Color.Rgb(255, 64, 64).WithAlpha(220), 1.5f);
            c.FillRoundRect(new RectF(r.X, r.Y, 24, 16), 3, Color.Rgb(255, 64, 64).WithAlpha(200));
            c.DrawText(label, new RectF(r.X, r.Y, 24, 16), Color.White, new TextStyle(11, Bold: true, Center: true));
        }

        Box("1", TableLayout.TopHandArea);      // 对家手牌
        Box("2", TableLayout.TopMeldArea);      // 对家副露
        Box("3", TableLayout.LeftHandArea);     // 上家手牌
        Box("4", TableLayout.LeftMeldArea);     // 上家副露
        Box("5", TableLayout.RightHandArea);    // 下家手牌
        Box("6", TableLayout.RightMeldArea);    // 下家副露
        Box("7", TableLayout.HandArea);         // 自家手牌
        Box("8", TableLayout.OwnMeldArea);      // 自家副露
        Box("9", TableLayout.CenterPlate);      // 中央板

        var r0 = TableLayout.River(0);
        Box("A", r0.Bounds);
        Box("B", TableLayout.River(1).Bounds);
        Box("C", TableLayout.River(2).Bounds);
        Box("D", TableLayout.River(3).Bounds);
    }

    // ── 桌面 ──
    private static void DrawFelt(ICanvas c)
    {
        c.FillLinearGradient(new RectF(0, 0, TableLayout.Width, TableLayout.Height), 0,
            Color.Rgb(52, 68, 58), Color.Rgb(28, 40, 34));

        var t = TableLayout.Table;
        c.FillRoundRect(t.Inflate(4), 24, Color.Rgb(0, 0, 0).WithAlpha(120));
        c.FillLinearGradient(t, 20, Color.Rgb(74, 96, 70), Color.Rgb(40, 56, 44));
        c.StrokeRoundRect(t, 20, Color.Rgb(30, 40, 32), 2f);
        // 中央浅色区（牌河底）
        var inner = TableLayout.CenterPlate.Inflate(190);
        c.FillRoundRect(inner, 18, Color.Rgb(255, 255, 255).WithAlpha(12));
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
            float rowStepY = g.dyRow != 0 ? g.dyRow : 0;
            float rowStepX = g.dxRow != 0 ? g.dxRow : 0;

            // 行太多时压缩行距（不越界）
            float maxDepth = g.Bounds.W + g.Bounds.H > 0 ? Math.Max(g.Bounds.W, g.Bounds.H) : 150f;
            float needed = rows * Math.Abs(rowStepY != 0 ? rowStepY : rowStepX);
            float scale = needed > maxDepth ? maxDepth / needed : 1f;

            for (int i = 0; i < count; i++)
            {
                int row = i / TableLayout.RiverCols;
                int col = i % TableLayout.RiverCols;
                float x = g.Bounds.X + col * g.dxCol + row * rowStepX * scale;
                float y = g.Bounds.Y + col * g.dyCol + row * rowStepY * scale;

                var slot = g.angle == 0f || g.angle == 180f
                    ? new RectF(x, y, TableLayout.RiverTileW, TableLayout.RiverTileH)
                    : new RectF(x, y, TableLayout.RiverTileH, TableLayout.RiverTileW);

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
        c.FillRoundRect(p.Inflate(5), 18, Color.Rgb(0, 0, 0).WithAlpha(110));
        c.FillLinearGradient(p, 16, Color.Rgb(84, 96, 108), Color.Rgb(46, 54, 64));
        c.StrokeRoundRect(p, 16, Color.Rgb(24, 28, 34), 2f);
        c.StrokeRoundRect(p.Inflate(-6), 12, Color.Rgb(255, 255, 255).WithAlpha(24), 1f);

        // 四家点数（沿四边）
        DrawPlateScore(c, Seat(vm, 0), p.CenterX, p.Bottom - 26);
        DrawPlateScore(c, Seat(vm, 2), p.CenterX, p.Y + 26);
        DrawPlateScore(c, Seat(vm, 3), p.X + 34, p.CenterY);
        DrawPlateScore(c, Seat(vm, 1), p.Right - 34, p.CenterY);

        // 中央：局数 / 剩余
        c.DrawText(vm.RoundLabel, new RectF(p.X, p.CenterY - 34, p.W, 30),
            Color.Rgb(140, 246, 236), new TextStyle(20, Bold: true, Center: true));
        c.DrawText("余 " + vm.WallCount, new RectF(p.X, p.CenterY + 2, p.W, 26),
            Color.Rgb(150, 236, 226), new TextStyle(17, Center: true));
        c.DrawText("本场 " + vm.Honba + " · 供托 " + vm.Kyotaku, new RectF(p.X, p.CenterY + 30, p.W, 22),
            Color.Rgb(180, 214, 208), new TextStyle(13, Center: true));

        // 四家风牌（四角），与座位对应
        DrawWindBadge(c, Seat(vm, 0).Wind, p.X + 6, p.Bottom - 38);
        DrawWindBadge(c, Seat(vm, 1).Wind, p.Right - 34, p.Bottom - 38);
        DrawWindBadge(c, Seat(vm, 2).Wind, p.Right - 34, p.Y + 6);
        DrawWindBadge(c, Seat(vm, 3).Wind, p.X + 6, p.Y + 6);
    }

    private static void DrawPlateScore(ICanvas c, SeatView seat, float cx, float cy)
    {
        var box = new RectF(cx - 46, cy - 12, 92, 24);
        c.DrawText(seat.Score.ToString("N0"), box,
            seat.IsTurn ? Color.Rgb(255, 226, 140) : Color.Rgb(236, 240, 236),
            new TextStyle(16, Bold: seat.IsTurn, Center: true));
    }

    private static void DrawWindBadge(ICanvas c, string wind, float x, float y)
    {
        var r = new RectF(x, y, 28, 28);
        c.FillRoundRect(r, 6, Color.Rgb(30, 36, 42));
        c.StrokeRoundRect(r, 6, Color.Rgb(120, 132, 140), 1f);
        c.DrawText(wind, r, Color.Rgb(240, 244, 240), new TextStyle(16, Bold: true, Center: true));
    }

    // ── 对家（上）──
    private static void DrawTopSeat(ICanvas c, TableViewModel vm)
    {
        var seat = Seat(vm, 2);
        var area = TableLayout.TopHandArea;
        for (int i = 0; i < 13; i++)
        {
            var r = new RectF(area.X + i * (TableLayout.BackTileW + TableLayout.BackGap), area.Y,
                TableLayout.BackTileW, TableLayout.BackTileH);
            DrawBack(c, r);
        }

        DrawMeldsCentered(c, seat, TableLayout.TopMeldArea, horizontal: true, angle: 180f);
    }

    // ── 上家（左）/ 下家（右）──
    private static void DrawLeftSeat(ICanvas c, TableViewModel vm)
    {
        var seat = Seat(vm, 3);
        var area = TableLayout.LeftHandArea;
        for (int i = 0; i < 13; i++)
        {
            var r = new RectF(area.X, area.Y + i * TableLayout.SideBackStep,
                TableLayout.SideBackW, TableLayout.SideBackH);
            DrawBack(c, r);
        }

        DrawMeldsCentered(c, seat, TableLayout.LeftMeldArea, horizontal: false, angle: -90f);
    }

    private static void DrawRightSeat(ICanvas c, TableViewModel vm)
    {
        var seat = Seat(vm, 1);
        var area = TableLayout.RightHandArea;
        for (int i = 0; i < 13; i++)
        {
            var r = new RectF(area.X, area.Y + i * TableLayout.SideBackStep,
                TableLayout.SideBackW, TableLayout.SideBackH);
            DrawBack(c, r);
        }

        DrawMeldsCentered(c, seat, TableLayout.RightMeldArea, horizontal: false, angle: 90f);
    }

    // ── 自家 ──
    private static void DrawOwnSeat(ICanvas c, TableViewModel vm)
    {
        var seat = Seat(vm, 0);
        var area = TableLayout.HandArea;
        int total = seat.Hand.Count;
        int row1 = Math.Min(total, 13);
        int extra = total - row1;   // 摸牌（第 14 张）单独放右边空一格

        float step = TableLayout.HandTileW + TableLayout.HandGap;
        float width1 = row1 * step - TableLayout.HandGap + (extra > 0 ? TableLayout.DrawnGap + TableLayout.HandTileW : 0);
        float x = area.X + (area.W - width1) / 2f;

        for (int i = 0; i < row1; i++)
        {
            c.DrawTile(seat.Hand[i].Id, new RectF(x + i * step, area.Y, TableLayout.HandTileW, TableLayout.HandTileH));
        }

        for (int i = 0; i < extra; i++)
        {
            float dx = x + row1 * step - TableLayout.HandGap + TableLayout.DrawnGap;
            c.DrawTile(seat.Hand[row1 + i].Id, new RectF(dx, area.Y, TableLayout.HandTileW, TableLayout.HandTileH));
        }

        // 自家副露：贴在手牌右端，牌转 90°（同雀魂）
        DrawOwnMelds(c, seat, x + width1 + 12f);
    }

    // ── 副露 ──
    private static void DrawMelds(ICanvas c, SeatView seat, RectF area, bool horizontal, float angle, bool rightToLeft = false)
    {
        if (seat.Melds.Count == 0)
        {
            return;
        }

        const float w = TableLayout.RiverTileW;
        const float h = TableLayout.RiverTileH;
        const float gap = 2f;

        if (horizontal)
        {
            float x = rightToLeft ? area.Right - w : area.X;
            float y = area.Y + (area.H - h) / 2f;
            foreach (var meld in seat.Melds)
            {
                foreach (var tile in meld.Tiles)
                {
                    if (x + w > area.Right)
                    {
                        return;   // 区域满了就不再画，绝不越界压别人
                    }

                    DrawRotated(c, tile.Id, new RectF(x, y, w, h), angle);
                    x += rightToLeft ? -(w + gap) : (w + gap);
                }

                x += rightToLeft ? -6 : 6;
            }
        }
        else
        {
            float x = area.X + (area.W - w) / 2f;
            float y = area.Y;
            foreach (var meld in seat.Melds)
            {
                foreach (var tile in meld.Tiles)
                {
                    if (y + h > area.Bottom)
                    {
                        return;
                    }

                    DrawRotated(c, tile.Id, new RectF(x, y, w, h), angle);
                    y += w + gap;
                }

                y += 6;
            }
        }
    }


    /// <summary>对手副露：在给定区域里居中排（对家横排、上下家竖排）。</summary>
    private static void DrawMeldsCentered(ICanvas c, SeatView seat, RectF area, bool horizontal, float angle)
    {
        if (seat.Melds.Count == 0)
        {
            return;
        }

        const float w = TableLayout.RiverTileW;
        const float h = TableLayout.RiverTileH;
        const float gap = 2f;

        // 先算总长，再居中放
        int total = 0;
        foreach (var meld in seat.Melds)
        {
            total += meld.Tiles.Count;
        }

        float len = total * (horizontal ? w + gap : w + gap) + (seat.Melds.Count - 1) * 6f;

        if (horizontal)
        {
            float x = area.CenterX - len / 2f;
            float y = area.Y + (area.H - h) / 2f;
            foreach (var meld in seat.Melds)
            {
                foreach (var tile in meld.Tiles)
                {
                    DrawRotated(c, tile.Id, new RectF(x, y, w, h), angle);
                    x += w + gap;
                }

                x += 6;
            }
        }
        else
        {
            float x = area.X + (area.W - w) / 2f;
            float y = area.CenterY - len / 2f;
            foreach (var meld in seat.Melds)
            {
                foreach (var tile in meld.Tiles)
                {
                    DrawRotated(c, tile.Id, new RectF(x, y, w, h), angle);
                    y += w + gap;
                }

                y += 6;
            }
        }
    }

    /// <summary>自家副露：跟手牌一样大的牌，接在手牌右端。</summary>
    private static void DrawOwnMelds(ICanvas c, SeatView seat, float startX)
    {
        if (seat.Melds.Count == 0)
        {
            return;
        }

        float w = TableLayout.HandTileW;
        float h = TableLayout.HandTileH;
        float x = startX;
        float y = TableLayout.OwnMeldArea.Y + (TableLayout.OwnMeldArea.H - h) / 2f;

        foreach (var meld in seat.Melds)
        {
            foreach (var tile in meld.Tiles)
            {
                if (x + w > TableLayout.OwnMeldArea.Right)
                {
                    return;
                }

                c.DrawTile(tile.Id, new RectF(x, y, w, h));
                x += w + 1f;
            }

            x += 8;
        }
    }

    // ── 通用绘制 ──
    private static void DrawBack(ICanvas c, RectF r)
    {
        c.FillRoundRect(new RectF(r.X + 1, r.Y + 1, r.W, r.H), 4, Color.Rgb(0, 0, 0).WithAlpha(80));
        c.FillLinearGradient(r, 4, Color.Rgb(52, 138, 198), Color.Rgb(28, 86, 138));
        c.StrokeRoundRect(r, 4, Color.Rgb(16, 40, 62), 1f);
    }

    /// <summary>绕槽位中心旋转绘制牌面（侧边/对家牌用）。</summary>
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

    private static SeatView Seat(TableViewModel vm, int index) =>
        index < vm.Seats.Count ? vm.Seats[index] : new SeatView { Index = index };
}
