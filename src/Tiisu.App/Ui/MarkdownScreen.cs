using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.App.Ui.Markdown;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>七子风 Markdown 文档页：深色卡片、金色标题、圆体正文，分页阅读。</summary>
public sealed class MarkdownScreen : Screen
{
    private sealed record Line(string Text, TextStyle Style, float Indent, float Height, Color Color);

    private readonly Action _back;
    private readonly List<Line> _lines = new();
    private readonly List<(int Start, int Count)> _pages = new();
    private readonly string _title;
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
        _title = string.IsNullOrWhiteSpace(title) ? "文档" : title;
        _back = back;
        Build(MarkdownDocument.Parse(markdown));
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText(_title, new RectF(0, 36, Width, 46),
            Color.Rgb(242, 230, 190), new TextStyle(34, Bold: true, Center: true));
        c.DrawText("第 " + (_page + 1) + " / " + Math.Max(1, _pages.Count) + " 页",
            new RectF(0, 86, Width, 24), Color.Rgb(159, 180, 170), new TextStyle(14, Center: true));

        var card = new RectF(110, 118, Width - 220, 490);
        c.FillRoundRect(card, 16, Color.Rgb(24, 36, 32));
        c.StrokeRoundRect(card, 16, Color.Rgb(90, 114, 100), 1.2f);

        if (_pages.Count > 0)
        {
            var (start, count) = _pages[_page];
            float y = card.Y + 16;
            for (int i = start; i < start + count && i < _lines.Count; i++)
            {
                Line line = _lines[i];
                c.DrawText(line.Text,
                    new RectF(card.X + 24 + line.Indent, y, card.W - 48 - line.Indent, line.Height),
                    line.Color, line.Style);
                y += line.Height + LineGap;
            }
        }

        c.DrawText("← / → 翻页 · 滚轮翻页 · Esc 返回",
            new RectF(0, 616, Width, 22), Color.Rgb(127, 145, 137), new TextStyle(13, Center: true));

        _prev = new RectF(Width / 2f - 270, 650, 150, 44);
        _backButton = new RectF(Width / 2f - 75, 650, 150, 44);
        _next = new RectF(Width / 2f + 120, 650, 150, 44);
        DrawButton(c, _prev, "上一页", _prevHover, _page > 0);
        DrawButton(c, _backButton, "返回", _backHover, true);
        DrawButton(c, _next, "下一页", _nextHover, _page < _pages.Count - 1);
    }

    private static void DrawButton(ICanvas c, RectF rect, string label, bool hover, bool enabled)
    {
        var border = !enabled ? Color.Rgb(70, 84, 78) : hover ? Color.Rgb(242, 201, 76) : Color.Rgb(196, 168, 96);
        var fill = enabled
            ? hover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48)
            : Color.Rgb(28, 36, 32);
        c.FillRoundRect(rect, 10, fill);
        c.StrokeRoundRect(rect, 10, border, 1.3f);
        c.DrawText(label, rect, enabled ? Color.Rgb(242, 230, 190) : Color.Rgb(110, 124, 116),
            new TextStyle(17, Bold: true, Center: true));
    }

    public override bool OnClick(float x, float y)
    {
        if (_backButton.Contains(x, y))
        {
            _back();
            return true;
        }

        if (_prev.Contains(x, y) && _page > 0)
        {
            _page--;
            return true;
        }

        if (_next.Contains(x, y) && _page < _pages.Count - 1)
        {
            _page++;
            return true;
        }

        return false;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _prevHover = _page > 0 && _prev.Contains(x, y);
        _nextHover = _page < _pages.Count - 1 && _next.Contains(x, y);
        _backHover = _backButton.Contains(x, y);
        return _prevHover || _nextHover || _backHover;
    }

    public override bool OnWheel(float x, float y, int delta, bool shift)
    {
        if (delta == 0)
        {
            return false;
        }

        if (delta < 0 && _page < _pages.Count - 1)
        {
            _page++;
        }
        else if (delta > 0 && _page > 0)
        {
            _page--;
        }

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
                if (_page > 0)
                {
                    _page--;
                }

                return true;
            case Keys.Right:
            case Keys.PageDown:
                if (_page < _pages.Count - 1)
                {
                    _page++;
                }

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

        foreach (MarkdownBlock block in blocks)
        {
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
                    var style = new TextStyle(14, Bold: block.Header, Center: false);
                    foreach (string line in Wrap(block.Text, style, ContentWidth - 22))
                    {
                        Add(new Line(line, style, 8, 21, block.Header ? heading : text));
                    }

                    AddBlank(3);
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
