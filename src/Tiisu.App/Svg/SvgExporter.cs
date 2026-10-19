using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Tiisu.App.Table;
using Tiisu.App.Rendering;

namespace Tiisu.App.Svg;

/// <summary>
/// 把牌桌导出成可编辑 SVG（1280×720）：
///   · 牌面以 data URI 内嵌（34 张来源于 Web/mj_tiles/*.svg），单个文件即可打开编辑
///   · 每个区域一个 &lt;g id&gt;，方便北极星在矢量工具里改
///   · 另出一份 table-regions.svg，只含区域框 + 编号（用于标注版式）
/// </summary>
public sealed class SvgExporter
{
    private readonly Dictionary<string, string> _tileDataUris = new();

    public SvgExporter(string tilesDir)
    {
        if (!Directory.Exists(tilesDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(tilesDir, "*.svg"))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            byte[] bytes = File.ReadAllBytes(file);
            _tileDataUris[id] = "data:image/svg+xml;base64," + Convert.ToBase64String(bytes);
        }
    }

    public int TileCount => _tileDataUris.Count;

    public string Build(TableViewModel vm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"1280\" height=\"720\" viewBox=\"0 0 1280 720\">");
        sb.AppendLine("  <!-- 七子麻将牌桌（草稿）：区域用 <g id> 分组；牌面为内嵌 SVG，可直接替换 -->");
        sb.AppendLine("  <defs>");
        sb.AppendLine("    <linearGradient id=\"felt\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">");
        sb.AppendLine("      <stop offset=\"0\" stop-color=\"#4a6046\"/><stop offset=\"1\" stop-color=\"#283c2c\"/>");
        sb.AppendLine("    </linearGradient>");
        sb.AppendLine("    <linearGradient id=\"feltInner\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">");
        sb.AppendLine("      <stop offset=\"0\" stop-color=\"#2f5a4a\"/><stop offset=\"1\" stop-color=\"#1b3a30\"/>");
        sb.AppendLine("    </linearGradient>");
        sb.AppendLine("    <linearGradient id=\"plate\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">");
        sb.AppendLine("      <stop offset=\"0\" stop-color=\"#54606c\"/><stop offset=\"1\" stop-color=\"#2e3640\"/>");
        sb.AppendLine("    </linearGradient>");
        sb.AppendLine("  </defs>");

        // 桌面
        sb.AppendLine("  <g id=\"table\">");
        sb.AppendLine("    <rect x=\"0\" y=\"0\" width=\"1280\" height=\"720\" fill=\"url(#felt)\"/>");
        sb.AppendLine("    <rect x=\"10\" y=\"10\" width=\"1260\" height=\"700\" rx=\"20\" fill=\"url(#feltInner)\" stroke=\"#1e2820\" stroke-width=\"2\"/>");
        sb.AppendLine("  </g>");

        // 牌河
        AppendRiver(sb, "river-self", vm, 0);
        AppendRiver(sb, "river-right", vm, 1);
        AppendRiver(sb, "river-top", vm, 2);
        AppendRiver(sb, "river-left", vm, 3);

        // 中央板
        AppendCenter(sb, vm);

        // 四家手牌与副露
        AppendBacks(sb, "top-hand", vm, 2, TableLayout.TopHandArea, horizontal: true);
        AppendBacks(sb, "left-hand", vm, 3, TableLayout.LeftHandArea, horizontal: false);
        AppendBacks(sb, "right-hand", vm, 1, TableLayout.RightHandArea, horizontal: false);

        AppendMelds(sb, "top-meld", vm, 2, TableLayout.TopMeldArea, horizontal: true, angle: 180f);
        AppendMelds(sb, "left-meld", vm, 3, TableLayout.LeftMeldArea, horizontal: false, angle: -90f);
        AppendMelds(sb, "right-meld", vm, 1, TableLayout.RightMeldArea, horizontal: false, angle: 90f);

        AppendOwnHand(sb, vm);
        AppendOwnMelds(sb, vm);

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    // ── 牌河 ──
    private void AppendRiver(StringBuilder sb, string id, TableViewModel vm, int seat)
    {
        var view = seat < vm.Seats.Count ? vm.Seats[seat] : new SeatView();
        var g = TableLayout.River(seat);
        sb.AppendLine($"  <g id=\"{id}\">");
        int count = Math.Min(view.Discards.Count, TableLayout.RiverCols * 4);
        int rows = Math.Max(1, (count + TableLayout.RiverCols - 1) / TableLayout.RiverCols);
        float depth = Math.Abs(g.dyRow) + Math.Abs(g.dxRow);
        float band = Math.Max(g.Bounds.W, g.Bounds.H);
        float scale = rows * depth > band ? band / (rows * depth) : 1f;

        for (int i = 0; i < count; i++)
        {
            int row = i / TableLayout.RiverCols;
            int col = i % TableLayout.RiverCols;
            float x = g.Bounds.X + col * g.dxCol + row * g.dxRow * scale;
            float y = g.Bounds.Y + col * g.dyCol + row * g.dyRow * scale;
            AppendTile(sb, view.Discards[i].Id, x, y, TableLayout.RiverTileW, TableLayout.RiverTileH, g.angle);
        }

        sb.AppendLine("  </g>");
    }

    // ── 中央板 ──
    private static void AppendCenter(StringBuilder sb, TableViewModel vm)
    {
        var p = TableLayout.CenterPlate;
        sb.AppendLine("  <g id=\"center-plate\">");
        sb.AppendLine($"    <rect x=\"{F(p.X)}\" y=\"{F(p.Y)}\" width=\"{F(p.W)}\" height=\"{F(p.H)}\" rx=\"16\" fill=\"url(#plate)\" stroke=\"#181c22\" stroke-width=\"2\"/>");
        sb.AppendLine($"    <text x=\"{F(p.CenterX)}\" y=\"{F(p.CenterY - 18)}\" fill=\"#8cf6ec\" font-family=\"Microsoft YaHei\" font-size=\"20\" font-weight=\"bold\" text-anchor=\"middle\">{Esc(vm.RoundLabel)}</text>");
        sb.AppendLine($"    <text x=\"{F(p.CenterX)}\" y=\"{F(p.CenterY + 14)}\" fill=\"#96ece2\" font-family=\"Microsoft YaHei\" font-size=\"17\" text-anchor=\"middle\">余 {vm.WallCount}</text>");
        sb.AppendLine($"    <text x=\"{F(p.CenterX)}\" y=\"{F(p.CenterY + 40)}\" fill=\"#b4d6d0\" font-family=\"Microsoft YaHei\" font-size=\"13\" text-anchor=\"middle\">本场 {vm.Honba} · 供托 {vm.Kyotaku}</text>");

        AppendPlateScore(sb, "score-bottom", vm, 0, p.CenterX, p.Bottom - 22);
        AppendPlateScore(sb, "score-top", vm, 2, p.CenterX, p.Y + 30);
        AppendPlateScore(sb, "score-left", vm, 3, p.X + 36, p.CenterY + 62);
        AppendPlateScore(sb, "score-right", vm, 1, p.Right - 36, p.CenterY + 62);

        AppendWind(sb, "wind-self", vm, 0, p.X + 8, p.Bottom - 36);
        AppendWind(sb, "wind-right", vm, 1, p.Right - 36, p.Bottom - 36);
        AppendWind(sb, "wind-top", vm, 2, p.Right - 36, p.Y + 8);
        AppendWind(sb, "wind-left", vm, 3, p.X + 8, p.Y + 8);
        sb.AppendLine("  </g>");
    }

    private static void AppendPlateScore(StringBuilder sb, string id, TableViewModel vm, int seat, float cx, float cy)
    {
        var s = seat < vm.Seats.Count ? vm.Seats[seat] : new SeatView();
        sb.AppendLine($"    <text id=\"{id}\" x=\"{F(cx)}\" y=\"{F(cy)}\" fill=\"{(s.IsTurn ? "#ffe28c" : "#ecf0ec")}\" font-family=\"Microsoft YaHei\" font-size=\"16\" text-anchor=\"middle\">{s.Score:N0}</text>");
    }

    private static void AppendWind(StringBuilder sb, string id, TableViewModel vm, int seat, float x, float y)
    {
        var s = seat < vm.Seats.Count ? vm.Seats[seat] : new SeatView();
        sb.AppendLine($"    <g id=\"{id}\"><rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"28\" height=\"28\" rx=\"6\" fill=\"#1e242a\" stroke=\"#78848c\"/>"
            + $"<text x=\"{F(x + 14)}\" y=\"{F(y + 20)}\" fill=\"#f0f4f0\" font-family=\"Microsoft YaHei\" font-size=\"16\" font-weight=\"bold\" text-anchor=\"middle\">{Esc(s.Wind)}</text></g>");
    }

    // ── 手牌/副露 ──
    private void AppendBacks(StringBuilder sb, string id, TableViewModel vm, int seat, RectF area, bool horizontal)
    {
        sb.AppendLine($"  <g id=\"{id}\">");
        for (int i = 0; i < 13; i++)
        {
            var r = horizontal
                ? new RectF(area.X + i * (TableLayout.BackTileW + TableLayout.BackGap), area.Y, TableLayout.BackTileW, TableLayout.BackTileH)
                : new RectF(area.X, area.Y + i * TableLayout.SideBackStep, TableLayout.SideBackW, TableLayout.SideBackH);
            sb.AppendLine($"    <rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\" rx=\"4\" fill=\"#2e8ac6\" stroke=\"#10283e\"/>");
        }

        sb.AppendLine("  </g>");
    }

    private void AppendMelds(StringBuilder sb, string id, TableViewModel vm, int seat, RectF area, bool horizontal, float angle)
    {
        if (seat >= vm.Seats.Count || vm.Seats[seat].Melds.Count == 0)
        {
            return;
        }

        var seatView = vm.Seats[seat];
        sb.AppendLine($"  <g id=\"{id}\">");
        int total = 0;
        foreach (var meld in seatView.Melds)
        {
            total += meld.Tiles.Count;
        }

        float len = total * (TableLayout.RiverTileW + 2f) + (seatView.Melds.Count - 1) * 6f;
        float x = horizontal ? area.CenterX - len / 2f : area.X + (area.W - TableLayout.RiverTileW) / 2f;
        float y = horizontal ? area.Y + (area.H - TableLayout.RiverTileH) / 2f : area.CenterY - len / 2f;

        foreach (var meld in seatView.Melds)
        {
            foreach (var tile in meld.Tiles)
            {
                AppendTile(sb, tile.Id, x, y, TableLayout.RiverTileW, TableLayout.RiverTileH, angle);
                if (horizontal)
                {
                    x += TableLayout.RiverTileW + 2f;
                }
                else
                {
                    y += TableLayout.RiverTileW + 2f;
                }
            }

            if (horizontal)
            {
                x += 6;
            }
            else
            {
                y += 6;
            }
        }

        sb.AppendLine("  </g>");
    }

    private void AppendOwnHand(StringBuilder sb, TableViewModel vm)
    {
        var seat = vm.Seats.Count > 0 ? vm.Seats[0] : new SeatView();
        var area = TableLayout.HandArea;
        int total = seat.Hand.Count;
        int row1 = Math.Min(total, 13);
        int extra = total - row1;
        float step = TableLayout.HandTileW + TableLayout.HandGap;
        float width1 = row1 * step - TableLayout.HandGap + (extra > 0 ? TableLayout.DrawnGap + TableLayout.HandTileW : 0);
        float x = area.X + (area.W - width1) / 2f;
        float y = area.Y + (area.H - TableLayout.HandTileH) / 2f;

        sb.AppendLine("  <g id=\"own-hand\">");
        for (int i = 0; i < row1; i++)
        {
            AppendTile(sb, seat.Hand[i].Id, x + i * step, y, TableLayout.HandTileW, TableLayout.HandTileH, 0f);
        }

        for (int i = 0; i < extra; i++)
        {
            float dx = x + row1 * step - TableLayout.HandGap + TableLayout.DrawnGap;
            AppendTile(sb, seat.Hand[row1 + i].Id, dx, y, TableLayout.HandTileW, TableLayout.HandTileH, 0f);
        }

        sb.AppendLine("  </g>");
    }

    private void AppendOwnMelds(StringBuilder sb, TableViewModel vm)
    {
        var seat = vm.Seats.Count > 0 ? vm.Seats[0] : new SeatView();
        if (seat.Melds.Count == 0)
        {
            return;
        }

        // 接在手牌右端
        var area = TableLayout.HandArea;
        int total = Math.Min(seat.Hand.Count, 13);
        float step = TableLayout.HandTileW + TableLayout.HandGap;
        float handEnd = area.X + (area.W - (total * step - TableLayout.HandGap)) / 2f + total * step;

        sb.AppendLine("  <g id=\"own-meld\">");
        float x = handEnd + 12f;
        float y = area.Y + (area.H - TableLayout.HandTileH) / 2f;
        foreach (var meld in seat.Melds)
        {
            foreach (var tile in meld.Tiles)
            {
                AppendTile(sb, tile.Id, x, y, TableLayout.HandTileW, TableLayout.HandTileH, 0f);
                x += TableLayout.HandTileW + 1f;
            }

            x += 8;
        }

        sb.AppendLine("  </g>");
    }

    // ── 单张牌 ──
    private void AppendTile(StringBuilder sb, string id, float x, float y, float w, float h, float angle)
    {
        string href = _tileDataUris.TryGetValue(id, out var uri) ? uri : string.Empty;
        string transform = Math.Abs(angle) < 0.01f
            ? string.Empty
            : $" transform=\"rotate({F(angle)} {F(x + w / 2f)} {F(y + h / 2f)})\"";
        sb.AppendLine($"    <image x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(w)}\" height=\"{F(h)}\" href=\"{href}\" xlink:href=\"{href}\"{transform}/>");
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ── 区域标注版（只含框和编号）──
    public static string BuildRegions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1280\" height=\"720\" viewBox=\"0 0 1280 720\">");
        sb.AppendLine("  <rect width=\"1280\" height=\"720\" fill=\"#1a2420\"/>");
        void Box(string label, RectF r)
        {
            if (r.W <= 0 || r.H <= 0)
            {
                return;
            }

            sb.AppendLine($"  <g id=\"region-{label}\">");
            sb.AppendLine($"    <rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\" fill=\"none\" stroke=\"#ff4040\" stroke-width=\"1.5\"/>");
            sb.AppendLine($"    <rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"26\" height=\"16\" fill=\"#ff4040\"/>");
            sb.AppendLine($"    <text x=\"{F(r.X + 13)}\" y=\"{F(r.Y + 12.5f)}\" fill=\"#fff\" font-family=\"Consolas\" font-size=\"11\" text-anchor=\"middle\">{label}</text>");
            sb.AppendLine("  </g>");
        }

        Box("1-top-hand", TableLayout.TopHandArea);
        Box("2-top-meld", TableLayout.TopMeldArea);
        Box("3-left-hand", TableLayout.LeftHandArea);
        Box("4-left-meld", TableLayout.LeftMeldArea);
        Box("5-right-hand", TableLayout.RightHandArea);
        Box("6-right-meld", TableLayout.RightMeldArea);
        Box("7-own-hand", TableLayout.HandArea);
        Box("8-own-meld", TableLayout.OwnMeldArea);
        Box("9-center", TableLayout.CenterPlate);
        Box("A-river-self", TableLayout.River(0).Bounds);
        Box("B-river-right", TableLayout.River(1).Bounds);
        Box("C-river-top", TableLayout.River(2).Bounds);
        Box("D-river-left", TableLayout.River(3).Bounds);
        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
