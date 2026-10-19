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
    private sealed record TileArt(string Inner, float VbW, float VbH);

    private readonly Dictionary<string, TileArt> _tiles = new();

    public SvgExporter(string tilesDir)
    {
        if (!Directory.Exists(tilesDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(tilesDir, "*.svg"))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            var art = ParseTile(File.ReadAllText(file));
            if (art is not null)
            {
                _tiles[id] = art;
            }
        }
    }

    public int TileCount => _tiles.Count;

    /// <summary>读一张牌 SVG：取 viewBox、把 .cls-N 的 CSS 展开成 inline style、去掉根标签。</summary>
    private static TileArt? ParseTile(string text)
    {
        float vbW = 19f, vbH = 26f;
        var vb = System.Text.RegularExpressions.Regex.Match(text, "viewBox=\"([^\"]+)\"");
        if (vb.Success)
        {
            var parts = vb.Groups[1].Value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            {
                vbW = w;
                vbH = h;
            }
        }

        var css = new Dictionary<string, string>();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(text, @"\.(cls-\d+)\s*\{([^}]*)\}"))
        {
            css[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }

        string body = System.Text.RegularExpressions.Regex.Replace(
            text, @"<style>.*?</style>", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        body = System.Text.RegularExpressions.Regex.Replace(body, @"<defs>\s*</defs>", string.Empty);

        int svgStart = body.IndexOf("<svg", StringComparison.Ordinal);
        int open = svgStart >= 0 ? body.IndexOf('>', svgStart) : -1;
        int close = body.LastIndexOf("</svg>", StringComparison.Ordinal);
        if (open < 0 || close < 0 || close <= open)
        {
            return null;
        }

        string inner = body.Substring(open + 1, close - open - 1).Trim();
        inner = System.Text.RegularExpressions.Regex.Replace(inner, "class=\"([^\"]+)\"", m =>
        {
            var sbStyle = new StringBuilder("style=\"");
            bool any = false;
            foreach (var cls in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (css.TryGetValue(cls, out var decl))
                {
                    sbStyle.Append(decl);
                    if (!decl.TrimEnd().EndsWith(";"))
                    {
                        sbStyle.Append(';');
                    }

                    any = true;
                }
            }

            return any ? sbStyle.Append('"').ToString() : "\"\"";
        });

        return new TileArt(inner, vbW, vbH);
    }

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

        AppendGuides(sb);

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

        var badges = new List<(string Id, RectF Rect)>(WindBadges());
        AppendWind(sb, badges[0].Id, vm, 0, badges[0].Rect);
        AppendWind(sb, badges[1].Id, vm, 1, badges[1].Rect);
        AppendWind(sb, badges[2].Id, vm, 2, badges[2].Rect);
        AppendWind(sb, badges[3].Id, vm, 3, badges[3].Rect);
        sb.AppendLine("  </g>");
    }

    private static void AppendPlateScore(StringBuilder sb, string id, TableViewModel vm, int seat, float cx, float cy)
    {
        var s = seat < vm.Seats.Count ? vm.Seats[seat] : new SeatView();
        sb.AppendLine($"    <text id=\"{id}\" x=\"{F(cx)}\" y=\"{F(cy)}\" fill=\"{(s.IsTurn ? "#ffe28c" : "#ecf0ec")}\" font-family=\"Microsoft YaHei\" font-size=\"16\" text-anchor=\"middle\">{s.Score:N0}</text>");
    }

    private static void AppendWind(StringBuilder sb, string id, TableViewModel vm, int seat, RectF r)
    {
        var s = seat < vm.Seats.Count ? vm.Seats[seat] : new SeatView();
        sb.AppendLine($"    <g id=\"{id}\">");
        sb.AppendLine($"      <rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\" rx=\"6\" fill=\"#1e242a\" stroke=\"#78848c\"/>");
        sb.AppendLine($"      <text x=\"{F(r.CenterX)}\" y=\"{F(r.Y + 20)}\" fill=\"#f0f4f0\" font-family=\"Microsoft YaHei\" font-size=\"16\" font-weight=\"bold\" text-anchor=\"middle\">{Esc(s.Wind)}</text>");
        sb.AppendLine("    </g>");
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

    // ── 边界参考层（含风向徽章实际矩形）──
    private static void AppendGuides(StringBuilder sb)
    {
        sb.AppendLine("  <g id=\"regions-guides\" fill=\"none\" stroke=\"#ff4040\" stroke-width=\"1\" stroke-dasharray=\"4 3\" opacity=\"0.85\">");
        void R(string id, RectF r) =>
            sb.AppendLine($"    <rect id=\"{id}\" x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\"/>");

        R("guide-top-hand", TableLayout.TopHandArea);
        R("guide-top-meld", TableLayout.TopMeldArea);
        R("guide-left-hand", TableLayout.LeftHandArea);
        R("guide-left-meld", TableLayout.LeftMeldArea);
        R("guide-right-hand", TableLayout.RightHandArea);
        R("guide-right-meld", TableLayout.RightMeldArea);
        R("guide-own-hand", TableLayout.HandArea);
        R("guide-own-meld", TableLayout.OwnMeldArea);
        R("guide-center", TableLayout.CenterPlate);
        for (int seat = 0; seat < 4; seat++)
        {
            R($"guide-river-{seat}", TableLayout.River(seat).Bounds);
        }

        foreach (var (id, r) in WindBadges())
        {
            sb.AppendLine($"    <rect id=\"guide-{id}\" x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\" stroke=\"#ffb020\"/>");
        }

        sb.AppendLine("  </g>");
    }

    /// <summary>中央板四角风向徽章的实际矩形（渲染与导出共用同一份坐标）。</summary>
    public static IEnumerable<(string Id, RectF Rect)> WindBadges()
    {
        var p = TableLayout.CenterPlate;
        yield return ("wind-self", new RectF(p.X + 8, p.Bottom - 36, 28, 28));
        yield return ("wind-right", new RectF(p.Right - 36, p.Bottom - 36, 28, 28));
        yield return ("wind-top", new RectF(p.Right - 36, p.Y + 8, 28, 28));
        yield return ("wind-left", new RectF(p.X + 8, p.Y + 8, 28, 28));
    }

    // ── 单张牌（内联矢量，Illustrator/Inkscape 直接可编辑）──
    private void AppendTile(StringBuilder sb, string id, float x, float y, float w, float h, float angle)
    {
        if (!_tiles.TryGetValue(id, out var art))
        {
            sb.AppendLine($"    <rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(w)}\" height=\"{F(h)}\" fill=\"#f0ead6\" stroke=\"#999\"/>");
            return;
        }

        float sx = w / art.VbW;
        float sy = h / art.VbH;
        float cx = x + w / 2f;
        float cy = y + h / 2f;
        string transform = Math.Abs(angle) < 0.01f
            ? $"translate({F(x)} {F(y)}) scale({F(sx)} {F(sy)})"
            : $"rotate({F(angle)} {F(cx)} {F(cy)}) translate({F(x)} {F(y)}) scale({F(sx)} {F(sy)})";
        sb.AppendLine($"    <g data-tile=\"{id}\" transform=\"{transform}\">");
        sb.AppendLine("      " + art.Inner);
        sb.AppendLine("    </g>");
    }

    // ── 元素定位清单（JSON / Markdown）──
    public static string BuildLayoutJson()
    {
        static string R(string id, RectF r, string note = "") =>
            $"{{\"id\":\"{id}\",\"x\":{F(r.X)},\"y\":{F(r.Y)},\"w\":{F(r.W)},\"h\":{F(r.H)},\"note\":\"{note}\"}}";

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"canvas\": { \"w\": 1280, \"h\": 720 },");
        sb.AppendLine("  \"tileSizes\": { \"hand\": [56,78], \"river\": [30,42], \"backTop\": [36,46], \"backSide\": [44,30], \"meld\": [30,42], \"handGap\": 2, \"riverGap\": 2 },");
        sb.AppendLine("  \"regions\": [");
        sb.AppendLine("    " + R("center-plate", TableLayout.CenterPlate, "中央牌桌板") + ",");
        sb.AppendLine("    " + R("top-hand", TableLayout.TopHandArea, "对家手牌") + ",");
        sb.AppendLine("    " + R("top-meld", TableLayout.TopMeldArea, "对家副露") + ",");
        sb.AppendLine("    " + R("left-hand", TableLayout.LeftHandArea, "上家手牌") + ",");
        sb.AppendLine("    " + R("left-meld", TableLayout.LeftMeldArea, "上家副露") + ",");
        sb.AppendLine("    " + R("right-hand", TableLayout.RightHandArea, "下家手牌") + ",");
        sb.AppendLine("    " + R("right-meld", TableLayout.RightMeldArea, "下家副露") + ",");
        sb.AppendLine("    " + R("own-hand", TableLayout.HandArea, "自家手牌") + ",");
        sb.AppendLine("    " + R("own-meld", TableLayout.OwnMeldArea, "自家副露") + ",");
        sb.AppendLine("    " + R("river-self", TableLayout.River(0).Bounds, "自家牌河") + ",");
        sb.AppendLine("    " + R("river-right", TableLayout.River(1).Bounds, "下家牌河") + ",");
        sb.AppendLine("    " + R("river-top", TableLayout.River(2).Bounds, "对家牌河") + ",");
        sb.AppendLine("    " + R("river-left", TableLayout.River(3).Bounds, "上家牌河"));
        sb.AppendLine("  ],");
        sb.AppendLine("  \"windBadges\": [");
        var badges = new List<string>();
        foreach (var (bid, br) in WindBadges())
        {
            badges.Add("    " + R(bid, br, "风向徽章"));
        }

        sb.AppendLine(string.Join(",\n", badges));
        sb.AppendLine("  ],");
        sb.AppendLine("  \"riverGeometry\": [");
        for (int seat = 0; seat < 4; seat++)
        {
            var g = TableLayout.River(seat);
            sb.AppendLine($"    {{ \"seat\": {seat}, \"angle\": {F(g.angle)}, \"dxCol\": {F(g.dxCol)}, \"dyCol\": {F(g.dyCol)}, \"dxRow\": {F(g.dxRow)}, \"dyRow\": {F(g.dyRow)} }}" + (seat < 3 ? "," : ""));
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string BuildLayoutMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 牌桌元素定位（1280×720 逻辑坐标）");
        sb.AppendLine();
        sb.AppendLine("由 `--export-svg` 自动生成；改版式时按此表给新坐标即可。");
        sb.AppendLine();
        sb.AppendLine("## 牌尺寸");
        sb.AppendLine();
        sb.AppendLine("| 用途 | 宽×高 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| 自家手牌 | 56 × 78 |");
        sb.AppendLine("| 牌河 | 30 × 42 |");
        sb.AppendLine("| 对家牌背 | 36 × 46 |");
        sb.AppendLine("| 上下家牌背 | 44 × 30 |");
        sb.AppendLine("| 副露（别家） | 30 × 42 |");
        sb.AppendLine("| 牌间距 | 2 |");
        sb.AppendLine();
        sb.AppendLine("## 区域矩形");
        sb.AppendLine();
        sb.AppendLine("| id | 说明 | x | y | w | h |");
        sb.AppendLine("|---|---|---|---|---|---|");
        void Row(string id, string note, RectF r) =>
            sb.AppendLine($"| `{id}` | {note} | {r.X:0} | {r.Y:0} | {r.W:0} | {r.H:0} |");

        Row("center-plate", "中央牌桌板", TableLayout.CenterPlate);
        Row("top-hand", "对家手牌", TableLayout.TopHandArea);
        Row("top-meld", "对家副露", TableLayout.TopMeldArea);
        Row("left-hand", "上家手牌", TableLayout.LeftHandArea);
        Row("left-meld", "上家副露", TableLayout.LeftMeldArea);
        Row("right-hand", "下家手牌", TableLayout.RightHandArea);
        Row("right-meld", "下家副露", TableLayout.RightMeldArea);
        Row("own-hand", "自家手牌", TableLayout.HandArea);
        Row("own-meld", "自家副露（接在手牌右端）", TableLayout.OwnMeldArea);
        Row("river-self", "自家牌河（A）", TableLayout.River(0).Bounds);
        Row("river-right", "下家牌河（B）", TableLayout.River(1).Bounds);
        Row("river-top", "对家牌河（C）", TableLayout.River(2).Bounds);
        Row("river-left", "上家牌河（D）", TableLayout.River(3).Bounds);

        sb.AppendLine();
        sb.AppendLine("## 风向徽章（中央板四角实际矩形）");
        sb.AppendLine();
        sb.AppendLine("| id | x | y | w | h |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var (bid, br) in WindBadges())
        {
            sb.AppendLine($"| `{bid}` | {br.X:0} | {br.Y:0} | {br.W:0} | {br.H:0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 牌河几何");
        sb.AppendLine();
        sb.AppendLine("起点在各自 `Bounds` 的左上角；第 i 张牌：`x = X + (i%6)*dxCol + (i/6)*dxRow`，`y` 同理。");
        sb.AppendLine();
        sb.AppendLine("| 座位 | 朝向角 | dxCol / dyCol | dxRow / dyRow |");
        sb.AppendLine("|---|---|---|---|");
        for (int seat = 0; seat < 4; seat++)
        {
            var g = TableLayout.River(seat);
            string name = seat switch { 0 => "自家", 1 => "下家", 2 => "对家", _ => "上家" };
            sb.AppendLine($"| {seat}（{name}） | {g.angle:0}° | {g.dxCol:0} / {g.dyCol:0} | {g.dxRow:0} / {g.dyRow:0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 中央板文字");
        sb.AppendLine();
        var p = TableLayout.CenterPlate;
        sb.AppendLine("| 元素 | 位置 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 局数 | 居中，y = {p.CenterY - 18:0} |");
        sb.AppendLine($"| 剩余 | 居中，y = {p.CenterY + 14:0} |");
        sb.AppendLine($"| 本场/供托 | 居中，y = {p.CenterY + 40:0} |");
        sb.AppendLine($"| 下方点数 | ({p.CenterX:0}, {p.Bottom - 22:0}) |");
        sb.AppendLine($"| 上方点数 | ({p.CenterX:0}, {p.Y + 30:0}) |");
        sb.AppendLine($"| 左右点数 | ({p.X + 36:0}, {p.CenterY + 62:0}) / ({p.Right - 36:0}, {p.CenterY + 62:0}) |");
        return sb.ToString();
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

            string text = $"{label}  x={r.X:0} y={r.Y:0} w={r.W:0} h={r.H:0}";
            float tbw = 22f + text.Length * 6.2f;
            sb.AppendLine($"  <g id=\"region-{label}\">");
            sb.AppendLine($"    <rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\" fill=\"none\" stroke=\"#ff4040\" stroke-width=\"1.5\"/>");
            sb.AppendLine($"    <rect x=\"{F(r.X)}\" y=\"{F(r.Y - 15)}\" width=\"{F(tbw)}\" height=\"15\" fill=\"#c02020\" opacity=\"0.92\"/>");
            sb.AppendLine($"    <text x=\"{F(r.X + 4)}\" y=\"{F(r.Y - 4)}\" fill=\"#fff\" font-family=\"Consolas\" font-size=\"11\">{text}</text>");
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
        foreach (var (wid, wr) in WindBadges())
        {
            Box(wid, wr);
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
