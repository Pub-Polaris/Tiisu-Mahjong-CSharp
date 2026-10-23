using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Tiisu.App.Table;
using Tiisu.App.Ui;

namespace Tiisu.App.Svg;

/// <summary>
/// 把当前牌桌导出成可编辑 SVG（画布尺寸取自 <see cref="TableLayoutSpec"/>，现为 1200×1200）。
///   · 牌面以 <see cref="Web/mj_tiles/*.svg"/> 的内联矢量内嵌，单文件即可在 Illustrator / Inkscape 编辑
///   · 每个区域一个 &lt;g id&gt;，几何全部来自 Yoga 算出的布局，不手写坐标
///   · <see cref="BuildUi"/> 额外合并动作按钮条（五个语义色按钮）与 HUD，供 Illustrator 一次改完
///   · 另出 table-regions.svg（只有区域框 + 编号）与 layout.json / layout.md（区域清单）
/// </summary>
public sealed class SvgExporter
{
    private sealed record TileArt(string Inner, float VbW, float VbH);

    private readonly Dictionary<string, TileArt> _tiles = new();
    private readonly TableLayoutSpec _l;

    public SvgExporter(string tilesDir, TableLayoutSpec layout)
    {
        _l = layout;
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

    public string Build(TableViewModel vm)
    {
        var sb = BeginSvg();
        AppendFelt(sb);
        AppendRivers(sb, vm);
        AppendCenter(sb, vm);
        AppendSeats(sb, vm);
        AppendGuides(sb);
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// 合并 UI 导出：牌桌底板 + 四家手牌/副露/牌河 + 动作按钮条（语义色）+ HUD。
    /// 不含红色布局参考层，方便北极星在 Illustrator 里直接改视觉。
    /// </summary>
    public string BuildUi(TableViewModel vm)
    {
        var sb = BeginSvg();
        AppendFelt(sb);
        AppendRivers(sb, vm);
        AppendCenter(sb, vm);
        AppendSeats(sb, vm);
        AppendActionBar(sb, vm);
        AppendHud(sb, vm);
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private StringBuilder BeginSvg()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(_l.CanvasW)}\" height=\"{F(_l.CanvasH)}\" " +
                      $"viewBox=\"0 0 {F(_l.CanvasW)} {F(_l.CanvasH)}\" data-name=\"Tiisu 牌桌 UI\">");
        sb.AppendLine("  <!-- 七子麻将牌桌：几何由 Yoga 布局导出；区域用 <g id> 分组；牌面为内联矢量，可直接替换 -->");
        sb.AppendLine("  <defs>");
        sb.AppendLine("    <linearGradient id=\"felt\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">");
        sb.AppendLine("      <stop offset=\"0\" stop-color=\"#2f4a3a\"/><stop offset=\"1\" stop-color=\"#26412f\"/>");
        sb.AppendLine("    </linearGradient>");
        sb.AppendLine("    <linearGradient id=\"plate\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">");
        sb.AppendLine("      <stop offset=\"0\" stop-color=\"#3a4450\"/><stop offset=\"1\" stop-color=\"#2b333d\"/>");
        sb.AppendLine("    </linearGradient>");
        sb.AppendLine("  </defs>");
        return sb;
    }

    // ── 桌面 ──
    private void AppendFelt(StringBuilder sb)
    {
        float m = _l.Margin;
        sb.AppendLine("  <g id=\"table\" data-name=\"牌桌底板\">");
        sb.AppendLine($"    <rect x=\"0\" y=\"0\" width=\"{F(_l.CanvasW)}\" height=\"{F(_l.CanvasH)}\" fill=\"#141e18\"/>");
        sb.AppendLine($"    <rect x=\"{F(m)}\" y=\"{F(m)}\" width=\"{F(_l.CanvasW - m * 2)}\" height=\"{F(_l.CanvasH - m * 2)}\" " +
                      "rx=\"20\" fill=\"url(#felt)\"/>");
        sb.AppendLine("  </g>");
    }

    // ── 四家牌河 ──
    private void AppendRivers(StringBuilder sb, TableViewModel vm)
    {
        string[] names = { "river-self", "river-right", "river-top", "river-left" };
        string[] notes = { "自家牌河", "下家牌河", "对家牌河", "上家牌河" };

        for (int seat = 0; seat < 4; seat++)
        {
            var view = Seat(vm, seat);
            var g = _l.River(seat);
            sb.AppendLine($"  <g id=\"{names[seat]}\" data-name=\"{notes[seat]}\">");

            int count = Math.Min(view.Discards.Count, _l.RiverCols * _l.RiverRows);
            for (int i = 0; i < count; i++)
            {
                int row = i / _l.RiverCols;
                int col = i % _l.RiverCols;
                float x = g.Origin.X + col * g.dxCol + row * g.dxRow;
                float y = g.Origin.Y + col * g.dyCol + row * g.dyRow;

                float cx = Math.Abs(g.angle) == 90f ? x + _l.RiverH / 2f : x + _l.RiverW / 2f;
                float cy = Math.Abs(g.angle) == 90f ? y + _l.RiverW / 2f : y + _l.RiverH / 2f;

                AppendTile(sb, view.Discards[i].Id, cx, cy, g.angle, _l.RiverW, _l.RiverH);
            }

            sb.AppendLine("  </g>");
        }
    }

    // ── 中央板 ──
    private void AppendCenter(StringBuilder sb, TableViewModel vm)
    {
        var p = _l.PlateRect;
        sb.AppendLine("  <g id=\"center-plate\" data-name=\"中央牌桌板\">");
        sb.AppendLine($"    <rect x=\"{F(p.X)}\" y=\"{F(p.Y)}\" width=\"{F(p.W)}\" height=\"{F(p.H)}\" rx=\"16\" " +
                      "fill=\"url(#plate)\" stroke=\"#161c22\" stroke-width=\"2\"/>");
        AppendText(sb, vm.RoundLabel, p.CenterX, p.CenterY - 42, 26, "#8cf6ec", bold: true);
        AppendText(sb, "余 " + vm.WallCount, p.CenterX, p.CenterY + 2, 21, "#96ece2");
        AppendText(sb, $"本场 {vm.Honba} · 场赏 {vm.Kyotaku}", p.CenterX, p.CenterY + 34, 15, "#b4d6d0");

        const float s = 38f, m = 14f;
        var winds = new[] { Seat(vm, 0).Wind, Seat(vm, 1).Wind, Seat(vm, 2).Wind, Seat(vm, 3).Wind };
        AppendWind(sb, winds[0], new Rendering.RectF(p.X + m, p.Bottom - m - s, s, s));
        AppendWind(sb, winds[1], new Rendering.RectF(p.Right - m - s, p.Bottom - m - s, s, s));
        AppendWind(sb, winds[2], new Rendering.RectF(p.Right - m - s, p.Y + m, s, s));
        AppendWind(sb, winds[3], new Rendering.RectF(p.X + m, p.Y + m, s, s));
        sb.AppendLine("  </g>");
    }

    private static void AppendText(StringBuilder sb, string text, float cx, float cy, float size, string color, bool bold = false)
    {
        string weight = bold ? " font-weight=\"bold\"" : string.Empty;
        sb.AppendLine($"    <text x=\"{F(cx)}\" y=\"{F(cy)}\" fill=\"{color}\" font-family=\"Microsoft YaHei\" " +
                      $"font-size=\"{F(size)}\"{weight} text-anchor=\"middle\" dominant-baseline=\"middle\">{Esc(text)}</text>");
    }

    private static void AppendWind(StringBuilder sb, string wind, Rendering.RectF r)
    {
        sb.AppendLine($"    <rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\" rx=\"8\" " +
                      "fill=\"#1e242a\" stroke=\"#78848c\" stroke-width=\"1.5\"/>");
        AppendText(sb, wind, r.CenterX, r.CenterY, 21, "#f0f4f0", bold: true);
    }

    // ── 四家手牌 + 副露 ──
    private void AppendSeats(StringBuilder sb, TableViewModel vm)
    {
        string[] names = { "hand-self", "hand-right", "hand-top", "hand-left" };
        string[] notes = { "自家手牌", "下家手牌", "对家手牌", "上家手牌" };

        for (int seat = 0; seat < 4; seat++)
        {
            var view = Seat(vm, seat);
            var (_, angle, _) = TableLayoutSpec.Axis(seat);
            var rects = TableSceneSkia.HandTileRects(_l, view, seat);

            sb.AppendLine($"  <g id=\"{names[seat]}\" data-name=\"{notes[seat]}\">");
            for (int i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                if (seat == 0 && i < view.Hand.Count)
                {
                    AppendTile(sb, view.Hand[i].Id, r.CenterX, r.CenterY, angle, _l.HandW, _l.HandH);
                }
                else
                {
                    sb.AppendLine($"    <rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\" " +
                                  "rx=\"4\" fill=\"#2e8ac6\" stroke=\"#102a3e\" stroke-width=\"1\"/>");
                }
            }

            sb.AppendLine("  </g>");

            if ((view.Melds?.Count ?? 0) > 0)
            {
                sb.AppendLine($"  <g id=\"{names[seat].Replace("hand-", "meld-")}\" data-name=\"{notes[seat].Replace("手牌", "副露")}\">");
                bool afterWin = vm.InSettlement;
                var masked = new Dictionary<MeldView, Rendering.RectF>();
                foreach (var placement in TableSceneSkia.MeldTilePlacements(_l, view, seat))
                {
                    var slot = placement.Slot;
                    var meld = placement.Meld;
                    if (meld.IsFaceUp(placement.TileIndex, afterWin))
                    {
                        AppendTile(sb, meld.Tiles[placement.TileIndex].Id, slot.CenterX, slot.CenterY, angle, _l.MeldW, _l.MeldH);
                    }
                    else
                    {
                        sb.AppendLine($"    <rect x=\"{F(slot.X)}\" y=\"{F(slot.Y)}\" width=\"{F(slot.W)}\" height=\"{F(slot.H)}\" " +
                                      "rx=\"3\" fill=\"#2e8ac6\" stroke=\"#102a3e\" stroke-width=\"1\"/>");
                    }

                    if (meld.IsAnkan && (afterWin || meld.RevealedAfterWin))
                    {
                        masked[meld] = masked.TryGetValue(meld, out var acc)
                            ? Rendering.RectF.FromLTRB(
                                Math.Min(acc.Left, slot.Left),
                                Math.Min(acc.Top, slot.Top),
                                Math.Max(acc.Right, slot.Right),
                                Math.Max(acc.Bottom, slot.Bottom))
                            : slot;
                    }
                }

                foreach (var rect in masked.Values)
                {
                    sb.AppendLine($"    <rect x=\"{F(rect.X - 1)}\" y=\"{F(rect.Y - 1)}\" width=\"{F(rect.W + 2)}\" " +
                                  $"height=\"{F(rect.H + 2)}\" rx=\"4\" fill=\"#000000\" fill-opacity=\"0.3\"/>");
                }

                sb.AppendLine("  </g>");
            }
        }
    }

    // ── 动作按钮条 ──
    private void AppendActionBar(StringBuilder sb, TableViewModel vm)
    {
        var bar = new ActionBar();
        bar.Build(vm, _l);

        sb.AppendLine("  <g id=\"action-bar\" data-name=\"动作按钮条\">");
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in bar.Buttons)
        {
            string key = ActionBar.SemanticId(b.Kind);
            used.TryGetValue(key, out int n);
            used[key] = n + 1;
            string id = n == 0 ? $"btn-{key}" : $"btn-{key}-{n + 1}";
            var colors = ActionBar.ColorsFor(b.Kind, hover: false, enabled: b.Enabled);

            sb.AppendLine($"    <g id=\"{id}\" data-name=\"{Esc(b.Label)}\">");
            sb.AppendLine($"      <rect x=\"{F(b.Rect.X)}\" y=\"{F(b.Rect.Y)}\" width=\"{F(b.Rect.W)}\" " +
                          $"height=\"{F(b.Rect.H)}\" rx=\"10\" fill=\"{Hex(colors.Fill)}\" " +
                          $"stroke=\"{Hex(colors.Border)}\" stroke-width=\"1.5\"/>");
            AppendText(sb, b.Label, b.Rect.CenterX, b.Rect.CenterY, b.Label.Length > 4 ? 15 : 18,
                Hex(colors.Text), bold: true);
            sb.AppendLine("    </g>");
        }

        sb.AppendLine("  </g>");
    }

    // ── HUD ──
    private void AppendHud(StringBuilder sb, TableViewModel vm)
    {
        sb.AppendLine("  <g id=\"hud\" data-name=\"HUD 文字\">");

        var hint = _l.HudRect;
        sb.AppendLine($"    <rect x=\"{F(hint.X)}\" y=\"{F(hint.Y)}\" width=\"{F(hint.W)}\" height=\"{F(hint.H)}\" " +
                      "rx=\"8\" fill=\"#000000\" fill-opacity=\"0.51\"/>");
        sb.AppendLine($"    <text x=\"{F(hint.X + 12)}\" y=\"{F(hint.Y + 8 + 10)}\" fill=\"#e6ece2\" " +
                      "font-family=\"Microsoft YaHei\" font-size=\"15\" dominant-baseline=\"middle\">M / ESC：菜单</text>");

        if (!string.IsNullOrEmpty(vm.Message))
        {
            float w = 660f;
            var box = new Rendering.RectF(_l.CanvasW / 2f - w / 2f, 18, w, 38);
            sb.AppendLine($"    <rect x=\"{F(box.X)}\" y=\"{F(box.Y)}\" width=\"{F(box.W)}\" height=\"{F(box.H)}\" " +
                          "rx=\"10\" fill=\"#000000\" fill-opacity=\"0.59\"/>");
            AppendText(sb, vm.Message, box.CenterX, box.CenterY, 17, "#ffe478", bold: true);
        }

        string status = vm.IsCalled ? "已报听" : vm.WinBanned ? "本局禁和" : string.Empty;
        if (status.Length > 0)
        {
            var box = new Rendering.RectF(_l.CanvasW - 190, 18, 166, 34);
            sb.AppendLine($"    <rect x=\"{F(box.X)}\" y=\"{F(box.Y)}\" width=\"{F(box.W)}\" height=\"{F(box.H)}\" " +
                          "rx=\"10\" fill=\"#000000\" fill-opacity=\"0.59\"/>");
            AppendText(sb, status, box.CenterX, box.CenterY, 16,
                vm.IsCalled ? "#ffe28c" : "#f0968c", bold: true);
        }

        sb.AppendLine("  </g>");
    }

    // ── 区域参考层 ──
    private void AppendGuides(StringBuilder sb)
    {
        sb.AppendLine("  <g id=\"guides\" fill=\"none\" stroke=\"#ff4040\" stroke-width=\"1.5\" opacity=\"0.9\">");
        foreach (var (id, r) in RegionRects(_l))
        {
            sb.AppendLine($"    <rect id=\"{id}\" x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.W)}\" height=\"{F(r.H)}\"/>");
        }

        sb.AppendLine("  </g>");
    }

    /// <summary>区域清单：四家手牌框 + 中央板 + 四家牌河。绘制、标注、JSON 共用。</summary>
    public static IEnumerable<(string Id, string Note, Rendering.RectF Rect)> Regions(TableLayoutSpec l)
    {
        yield return ("hand-self", "自家手牌框", l.HandRects[0]);
        yield return ("hand-right", "下家手牌框", l.HandRects[1]);
        yield return ("hand-top", "对家手牌框", l.HandRects[2]);
        yield return ("hand-left", "上家手牌框", l.HandRects[3]);
        yield return ("center-plate", "中央牌桌板", l.PlateRect);
        yield return ("river-self", "自家牌河", l.River(0).Bounds);
        yield return ("river-right", "下家牌河", l.River(1).Bounds);
        yield return ("river-top", "对家牌河", l.River(2).Bounds);
        yield return ("river-left", "上家牌河", l.River(3).Bounds);
    }

    private static IEnumerable<(string Id, Rendering.RectF Rect)> RegionRects(TableLayoutSpec l)
    {
        foreach (var (id, _, rect) in Regions(l))
        {
            yield return (id, rect);
        }
    }

    public static string BuildRegions(TableLayoutSpec l)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(l.CanvasW)}\" height=\"{F(l.CanvasH)}\" " +
                      $"viewBox=\"0 0 {F(l.CanvasW)} {F(l.CanvasH)}\">");
        sb.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#0e1512\"/>");
        sb.AppendLine("  <g fill=\"none\" stroke=\"#ff4040\" stroke-width=\"1.5\">");
        foreach (var (id, rect) in RegionRects(l))
        {
            sb.AppendLine($"    <rect id=\"{id}\" x=\"{F(rect.X)}\" y=\"{F(rect.Y)}\" width=\"{F(rect.W)}\" height=\"{F(rect.H)}\"/>");
        }

        sb.AppendLine("  </g>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    public static string BuildLayoutJson(TableLayoutSpec l)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"canvas\": {{ \"w\": {F(l.CanvasW)}, \"h\": {F(l.CanvasH)} }},");
        sb.AppendLine($"  \"tileSizes\": {{ \"hand\": [{F(l.HandW)}, {F(l.HandH)}], \"meld\": [{F(l.MeldW)}, {F(l.MeldH)}], " +
                      $"\"river\": [{F(l.RiverW)}, {F(l.RiverH)}], \"handGap\": {F(l.HandGap)} }},");
        sb.AppendLine("  \"regions\": [");

        var rows = new List<string>();
        foreach (var (id, note, r) in Regions(l))
        {
            rows.Add($"    {{\"id\":\"{id}\",\"x\":{F(r.X)},\"y\":{F(r.Y)},\"w\":{F(r.W)},\"h\":{F(r.H)},\"note\":\"{Esc(note)}\"}}");
        }

        sb.AppendLine(string.Join(",\n", rows));
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string BuildLayoutMarkdown(TableLayoutSpec l)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 牌桌区域清单");
        sb.AppendLine();
        sb.AppendLine($"画布 {F(l.CanvasW)}×{F(l.CanvasH)}；几何由 Yoga 布局计算，改牌尺寸会整体重排。");
        sb.AppendLine();
        sb.AppendLine("| id | 说明 | x | y | 宽 | 高 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var (id, note, r) in Regions(l))
        {
            sb.AppendLine($"| `{id}` | {note} | {F(r.X)} | {F(r.Y)} | {F(r.W)} | {F(r.H)} |");
        }

        sb.AppendLine();
        sb.AppendLine("手牌框四家等大（14 张手牌宽 = 739）；副露贴在手牌框的右手侧，不参与手牌定位。");
        return sb.ToString();
    }

    // ── 单张牌 ──
    private void AppendTile(StringBuilder sb, string id, float cx, float cy, float angle, float w, float h)
    {
        if (!_tiles.TryGetValue(id, out var art))
        {
            sb.AppendLine($"    <rect x=\"{F(cx - w / 2f)}\" y=\"{F(cy - h / 2f)}\" width=\"{F(w)}\" height=\"{F(h)}\" " +
                          "rx=\"4\" fill=\"#f6f1e3\" stroke=\"#8a8a6a\"/>");
            return;
        }

        float sx = w / art.VbW;
        float sy = h / art.VbH;
        sb.AppendLine($"    <g transform=\"translate({F(cx)},{F(cy)}) rotate({F(angle)}) " +
                      $"scale({F(sx)},{F(sy)}) translate({F(-art.VbW / 2f)},{F(-art.VbH / 2f)})\">");
        sb.AppendLine("      " + art.Inner);
        sb.AppendLine("    </g>");
    }

    // ── 读一张牌 SVG：取 viewBox、把 .cls-N 的 CSS 展开成 inline style、去掉根标签 ──
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

    private static SeatView Seat(TableViewModel vm, int i) =>
        i < vm.Seats.Count ? vm.Seats[i] : new SeatView { Index = i };

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Hex(Rendering.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string Esc(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
