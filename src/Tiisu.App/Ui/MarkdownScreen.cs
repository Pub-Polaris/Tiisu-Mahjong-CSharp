using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.Localization;
using Tiisu.App.Ui.Markdown;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>七子风 Markdown 文档页：深色卡片、金色标题、圆体正文，分页阅读。</summary>
public sealed class MarkdownScreen : Screen
{
    private sealed record Line(
        string Text, TextStyle Style, float Indent, float Height, Color Color,
        IReadOnlyList<string>? Cells = null, float[]? Widths = null, bool Rule = false);

    private readonly Action _back;
    private readonly List<Line> _lines = new();
    private readonly List<(int Start, int Count)> _pages = new();
    private readonly string _title;
    private float _scroll;
    private float _maxScroll;
    private int _page;

    private RectF _prev;
    private RectF _next;
    private RectF _backButton;
    private bool _prevHover;
    private bool _nextHover;
    private bool _backHover;

    private const float ContentWidth = 950f;
    private const float PageHeight = 470f;
    private const float LineGap = 4f;

    public MarkdownScreen(string title, string markdown, Action back) : base("markdown")
    {
        _title = string.IsNullOrWhiteSpace(title) ? L.T("app.ui.markdown.defaultTitle") : title;
        _back = back;
        Build(MarkdownDocument.Parse(markdown));
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText(UiText.Tr(_title), new RectF(0, 36, Width, 46),
            Color.Rgb(242, 230, 190), new TextStyle(34, Bold: true, Center: true));
        c.DrawText(L.T("app.ui.markdown.pageIndicator", ("current", _page + 1), ("total", Math.Max(1, _pages.Count))),
            new RectF(0, 86, Width, 24), Color.Rgb(159, 180, 170), new TextStyle(14, Center: true));

        var card = new RectF(110, 118, Width - 220, 490);
        c.FillRoundRect(card, 16, Color.Rgb(24, 36, 32));
        c.StrokeRoundRect(card, 16, Color.Rgb(90, 114, 100), 1.2f);

        if (_lines.Count > 0)
        {
            float contentHeight = 16;
            foreach (Line line in _lines)
            {
                contentHeight += line.Height + LineGap;
            }

            _maxScroll = Math.Max(0, contentHeight - card.H + 16);
            _scroll = Math.Clamp(_scroll, 0, _maxScroll);
            _page = _pages.Count <= 0
                ? 0
                : Math.Clamp((int)(_scroll / PageHeight), 0, _pages.Count - 1);

            c.Save();
            c.ClipRect(new RectF(card.X + 1, card.Y + 1, card.W - 2, card.H - 2));
            c.Translate(0, -_scroll);
            float y = card.Y + 16;
            foreach (Line line in _lines)
            {
                if (line.Rule)
                {
                    c.FillRect(new RectF(card.X + 24, y + 1, card.W - 48, 1),
                        Color.Rgb(90, 114, 100).WithAlpha(150));
                }
                else if (line.Cells is { Count: > 0 } cells && line.Widths is { Length: > 0 } widths)
                {
                    DrawTableRow(c, card, y, line, cells, widths);
                }
                else
                {
                    c.DrawText(line.Text,
                        new RectF(card.X + 24 + line.Indent, y, card.W - 48 - line.Indent, line.Height),
                        line.Color, line.Style);
                }

                y += line.Height + LineGap;
            }

            c.Restore();

            if (_maxScroll > 0)
            {
                float trackTop = card.Y + 18;
                float trackHeight = card.H - 36;
                float thumbHeight = Math.Max(36, trackHeight * (card.H / (card.H + _maxScroll)));
                float thumbTop = trackTop + (trackHeight - thumbHeight) * (_scroll / _maxScroll);
                c.FillRoundRect(new RectF(card.Right - 9, trackTop, 4, trackHeight), 2,
                    Color.Rgb(80, 100, 92).WithAlpha(100));
                c.FillRoundRect(new RectF(card.Right - 9, thumbTop, 4, thumbHeight), 2,
                    Color.Rgb(196, 168, 96).WithAlpha(210));
            }
        }

        c.DrawText(L.T("app.ui.markdown.footer"),
            new RectF(0, 616, Width, 22), Color.Rgb(127, 145, 137), new TextStyle(13, Center: true));

        _prev = new RectF(Width / 2f - 270, 650, 150, 44);
        _backButton = new RectF(Width / 2f - 75, 650, 150, 44);
        _next = new RectF(Width / 2f + 120, 650, 150, 44);
        DrawButton(c, _prev, "app.ui.markdown.prev", _prevHover, _scroll > 0);
        DrawButton(c, _backButton, "app.ui.markdown.back", _backHover, true);
        DrawButton(c, _next, "app.ui.markdown.next", _nextHover, _scroll < _maxScroll);
    }

    private static void DrawTableRow(ICanvas c, RectF card, float y, Line line,
        IReadOnlyList<string> cells, float[] widths)
    {
        float x = card.X + 24 + line.Indent;
        for (int i = 0; i < cells.Count && i < widths.Length; i++)
        {
            c.DrawText(cells[i], new RectF(x, y, Math.Max(1, widths[i] - 12), line.Height),
                line.Color, line.Style);
            x += widths[i];
        }
    }

    private static void DrawButton(ICanvas c, RectF rect, string label, bool hover, bool enabled)
    {
        var border = !enabled ? Color.Rgb(70, 84, 78) : hover ? Color.Rgb(242, 201, 76) : Color.Rgb(196, 168, 96);
        var fill = enabled
            ? hover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48)
            : Color.Rgb(28, 36, 32);
        c.FillRoundRect(rect, 10, fill);
        c.StrokeRoundRect(rect, 10, border, 1.3f);
        c.DrawText(L.T(label), rect, enabled ? Color.Rgb(242, 230, 190) : Color.Rgb(110, 124, 116),
            new TextStyle(17, Bold: true, Center: true));
    }

    public override bool OnClick(float x, float y)
    {
        if (_backButton.Contains(x, y))
        {
            _back();
            return true;
        }

        if (_prev.Contains(x, y) && _scroll > 0)
        {
            _scroll = Math.Max(0, _scroll - PageHeight);
            return true;
        }

        if (_next.Contains(x, y) && _scroll < _maxScroll)
        {
            _scroll = Math.Min(_maxScroll, _scroll + PageHeight);
            return true;
        }

        return false;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _prevHover = _scroll > 0 && _prev.Contains(x, y);
        _nextHover = _scroll < _maxScroll && _next.Contains(x, y);
        _backHover = _backButton.Contains(x, y);
        return _prevHover || _nextHover || _backHover;
    }

    public override bool OnWheel(float x, float y, int delta, bool shift)
    {
        if (delta == 0)
        {
            return false;
        }

        _scroll = Math.Clamp(_scroll - delta * 0.55f, 0, _maxScroll);
        return true;
    }

    public override bool OnKey(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
                _back();
                return true;
            case Keys.Left:
            case Keys.PageUp:
                _scroll = Math.Max(0, _scroll - PageHeight);
                return true;
            case Keys.Right:
            case Keys.PageDown:
                _scroll = Math.Min(_maxScroll, _scroll + PageHeight);
                return true;
            default:
                return false;
        }
    }

    private void Build(IReadOnlyList<MarkdownBlock> blocks)
    {
        var heading = new Color(242, 201, 76);
        var text = Color.Rgb(220, 228, 220);
        var muted = Color.Rgb(159, 180, 170);

        for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            MarkdownBlock block = blocks[blockIndex];
            switch (block.Kind)
            {
                case MarkdownBlockKind.Heading:
                {
                    float size = block.Level switch { 1 => 28f, 2 => 24f, 3 => 20f, _ => 18f };
                    AddWrapped(block.Text, new TextStyle(size, Bold: true, Center: false), 0, heading);
                    AddBlank(8);
                    break;
                }

                case MarkdownBlockKind.Paragraph:
                    AddWrapped(block.Text, new TextStyle(16, Center: false), 0, text);
                    AddBlank(8);
                    break;

                case MarkdownBlockKind.Bullet:
                    foreach (string line in Wrap("· " + block.Text, new TextStyle(16, Center: false), ContentWidth - 22))
                    {
                        Add(new Line(line, new TextStyle(16, Center: false), 20, 24, text));
                    }

                    AddBlank(4);
                    break;

                case MarkdownBlockKind.Quote:
                    foreach (string line in Wrap("│ " + block.Text, new TextStyle(15, Center: false), ContentWidth - 22))
                    {
                        Add(new Line(line, new TextStyle(15, Center: false), 18, 23, muted));
                    }

                    AddBlank(6);
                    break;

                case MarkdownBlockKind.Code:
                    foreach (string raw in block.Text.Split('\n'))
                    {
                        string codeLine = raw.Length == 0 ? " " : raw;
                        Add(new Line(codeLine, new TextStyle(13, Center: false), 18, 19, Color.Rgb(196, 232, 190)));
                    }

                    AddBlank(8);
                    break;

                case MarkdownBlockKind.Table:
                {
                    var rows = new List<MarkdownBlock>();
                    while (blockIndex < blocks.Count && blocks[blockIndex].Kind == MarkdownBlockKind.Table)
                    {
                        rows.Add(blocks[blockIndex]);
                        blockIndex++;
                    }

                    blockIndex--;
                    if (rows.Count == 0 || rows[0].Cells is not { Count: > 0 })
                    {
                        break;
                    }

                    var widths = TableWidths(rows.Select(r => r.Cells!));
                    foreach (MarkdownBlock row in rows)
                    {
                        var style = new TextStyle(14, Bold: row.Header, Center: false);
                        Add(new Line(string.Empty, style, 8, 25,
                            row.Header ? heading : text, Cells: row.Cells, Widths: widths));
                        if (row.Header)
                        {
                            Add(new Line(string.Empty, style, 0, 1, muted, Rule: true));
                        }
                    }

                    AddBlank(4);
                    break;
                }

                case MarkdownBlockKind.Rule:
                    Add(new Line("────────────────────────────────", new TextStyle(14), 0, 22, muted));
                    AddBlank(4);
                    break;
            }
        }

        BuildPages();
    }

    private static float[] TableWidths(IEnumerable<IReadOnlyList<string>> rows)
    {
        var all = rows.ToList();
        int columns = all.Count == 0 ? 0 : all.Max(r => r.Count);
        if (columns == 0)
        {
            return Array.Empty<float>();
        }

        var style = new TextStyle(14, Center: false);
        var widths = new float[columns];
        for (int i = 0; i < all.Count; i++)
        {
            for (int c = 0; c < all[i].Count; c++)
            {
                widths[c] = Math.Max(widths[c], SkiaCanvas.Measure(all[i][c], style) + 24);
            }
        }

        float total = widths.Sum();
        float available = ContentWidth - 48;
        if (total > available)
        {
            float scale = available / total;
            for (int i = 0; i < widths.Length; i++)
            {
                widths[i] = Math.Max(48, widths[i] * scale);
            }
        }

        return widths;
    }

    private void AddWrapped(string value, TextStyle style, float indent, Color color)
    {
        foreach (string line in Wrap(value, style, ContentWidth - indent))
        {
            Add(new Line(line, style, indent, 26, color));
        }
    }

    private void Add(Line line) => _lines.Add(line);

    private void AddBlank(float height) => _lines.Add(new Line(string.Empty, new TextStyle(10), 0, height, Color.Transparent));

    private static IEnumerable<string> Wrap(string value, TextStyle style, float width)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            result.Add(string.Empty);
            return result;
        }

        var current = new System.Text.StringBuilder();
        foreach (string word in value.Split(' '))
        {
            if (current.Length == 0 && SkiaCanvas.Measure(word, style) > width)
            {
                // 中文长句没有空格：按字符切。
                var chunk = new System.Text.StringBuilder();
                foreach (char ch in word)
                {
                    string test = chunk.ToString() + ch;
                    if (chunk.Length > 0 && SkiaCanvas.Measure(test, style) > width)
                    {
                        result.Add(chunk.ToString());
                        chunk.Clear();
                    }

                    chunk.Append(ch);
                }

                if (chunk.Length > 0)
                {
                    result.Add(chunk.ToString());
                }

                continue;
            }

            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && SkiaCanvas.Measure(candidate, style) > width)
            {
                result.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
            else
            {
                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                current.Append(word);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result.Count == 0 ? new[] { string.Empty } : result;
    }

    private void BuildPages()
    {
        if (_lines.Count == 0)
        {
            _pages.Add((0, 0));
            return;
        }

        int start = 0;
        float y = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (y > 0 && y + _lines[i].Height > PageHeight)
            {
                _pages.Add((start, i - start));
                start = i;
                y = 0;
            }

            y += _lines[i].Height + LineGap;
        }

        _pages.Add((start, _lines.Count - start));
        _page = Math.Clamp(_page, 0, _pages.Count - 1);
    }
}
