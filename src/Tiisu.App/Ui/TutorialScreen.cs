using System;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>菜单「新手引导」：出牌 / 报听 / 和牌三步，最后一页可直接开一局。</summary>
public sealed class TutorialScreen : Screen
{
    private static readonly (string Title, string[] Lines)[] Pages =
    {
        ("第一步 · 出牌", new[]
        {
            "轮到你时，点最下面一排手牌中的一张。",
            "刚摸到的牌会单独放在最右，点它或点其他牌都可以。",
            "不确定打哪张时，先看上方「听牌」提示和剩余张数。",
        }),
        ("第二步 · 报听", new[]
        {
            "手牌进入听牌后，可以点「报听」。",
            "报听后锁手：之后不能改变听牌形，只有不破坏听牌形的暗杠例外。",
            "报听本身不加赋；报和时赋不足 7 会被判错和。",
        }),
        ("第三步 · 和牌与结算", new[]
        {
            "能自摸时点「和牌」，别人打出铳张时点「荣和」。",
            "结算页会列出役种、点数变化和局内调整。",
            "点「下一局」继续；整场结束后回菜单。",
        }),
    };

    private readonly Action _back;
    private readonly Action _start;
    private readonly Action _openRules;
    private int _page;
    private RectF _prev;
    private RectF _next;
    private RectF _backButton;
    private RectF _startButton;
    private bool _prevHover;
    private bool _nextHover;
    private bool _backHover;
    private bool _startHover;

    public TutorialScreen(Action back, Action start, Action? openRules = null) : base("tutorial")
    {
        _back = back;
        _start = start;
        _openRules = openRules ?? (() => { });
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText("新手引导", new RectF(0, 44, Width, 54),
            Color.Rgb(242, 230, 190), new TextStyle(40, Bold: true, Center: true));
        c.DrawText("三步上手 · 第 " + (_page + 1) + " / " + Pages.Length + " 页",
            new RectF(0, 100, Width, 24), Color.Rgb(159, 180, 170), new TextStyle(15, Center: true));

        var (title, lines) = Pages[_page];
        var card = new RectF(180, 150, Width - 360, 370);
        c.FillRoundRect(card, 16, Color.Rgb(26, 38, 34));
        c.StrokeRoundRect(card, 16, Color.Rgb(96, 122, 108), 1.2f);

        c.DrawText(title, new RectF(card.X + 30, card.Y + 24, card.W - 60, 34),
            Color.Rgb(242, 201, 76), new TextStyle(24, Bold: true, Center: false));
        float y = card.Y + 78;
        foreach (string line in lines)
        {
            c.DrawText("· " + line, new RectF(card.X + 30, y, card.W - 60, 30),
                Color.Rgb(220, 228, 220), new TextStyle(17, Center: false));
            y += 42;
        }

        _prev = new RectF(Width / 2f - 300, 574, 150, 44);
        _next = new RectF(Width / 2f - 75, 574, 150, 44);
        _backButton = new RectF(Width / 2f + 150, 574, 150, 44);
        _startButton = new RectF(Width / 2f - 100, 634, 200, 44);

        DrawButton(c, _prev, "上一步", _prevHover, _page > 0);
        DrawButton(c, _next, "下一步", _nextHover, _page < Pages.Length - 1);
        DrawButton(c, _backButton, "返回菜单", _backHover, true);
        string actionLabel = _page switch
        {
            0 => "试打一局",
            1 => "查看规则速查",
            _ => "开始一局",
        };
        DrawButton(c, _startButton, actionLabel, _startHover, true);
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

        if (_next.Contains(x, y) && _page < Pages.Length - 1)
        {
            _page++;
            return true;
        }

        if (_startButton.Contains(x, y))
        {
            if (_page == 1)
            {
                _openRules();
            }
            else
            {
                _start();
            }

            return true;
        }

        return false;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _prevHover = _page > 0 && _prev.Contains(x, y);
        _nextHover = _page < Pages.Length - 1 && _next.Contains(x, y);
        _backHover = _backButton.Contains(x, y);
        _startHover = _startButton.Contains(x, y);
        return _prevHover || _nextHover || _backHover || _startHover;
    }

    public override bool OnKey(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
                _back();
                return true;
            case Keys.Left:
                if (_page > 0)
                {
                    _page--;
                }

                return true;
            case Keys.Right:
            case Keys.Enter:
            case Keys.Space:
                if (_page < Pages.Length - 1)
                {
                    _page++;
                }
                else if (_page == 1)
                {
                    _openRules();
                }
                else
                {
                    _start();
                }

                return true;
            default:
                return false;
        }
    }
}
