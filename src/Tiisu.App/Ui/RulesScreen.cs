using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>菜单「规则速查」：只讲七子特有规则，不写完整麻将教程。</summary>
public sealed class RulesScreen : Screen
{
    private readonly Action _back;
    private readonly List<(string Title, string[] Lines)> _sections = new()
    {
        ("起和与报听", new[]
        {
            "手牌够 7 赋才能和；赋不够不能和。",
            "报听本身不加赋，也不给额外收益。",
            "听牌即可报听；报听后锁手，不破坏听牌形的暗杠除外。",
        }),
        ("错和惩罚", new[]
        {
            "报听后主动报和，实际赋 < 7 = 错和。",
            "向三家赔 Z = 赋 × 700。",
            "Z > 2100 重罚：禁和本局 + 下局。",
        }),
        ("场供与蓝筹", new[]
        {
            "结算按 350 点蓝筹取整。",
            "自摸除不尽的尾数进场供池。",
            "场供由下一手和牌者拿走；本场连庄有加成。",
        }),
    };

    private const string Rounded = "WenYuan Rounded SC";

    private RectF _backButton;

    public RulesScreen(Action back) : base("rules")
    {
        _back = back;
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText("规则速查", new RectF(0, 54, Width, 56),
            Color.Rgb(242, 230, 190), new TextStyle(42, Bold: true, Center: true, Family: Rounded));
        c.DrawText("七子麻将 · 和「标准日麻」不同的地方", new RectF(0, 108, Width, 26),
            Color.Rgb(159, 180, 170), new TextStyle(16, Center: true, Family: Rounded));

        float y = 136;
        const float lineH = 32f;
        foreach (var (title, lines) in _sections)
        {
            float boxH = 14f + 28f + lines.Length * lineH + 10f;
            var box = new RectF(150, y, Width - 300, boxH);
            c.FillRoundRect(box, 14, Color.Rgb(26, 38, 34));
            c.StrokeRoundRect(box, 14, Color.Rgb(96, 122, 108), 1.2f);
            c.DrawText(title, new RectF(box.X + 26, box.Y + 10, box.W - 52, 28),
                Color.Rgb(242, 201, 76), new TextStyle(20, Bold: true, Center: false, Family: Rounded));
            float ly = box.Y + 42;
            foreach (string line in lines)
            {
                c.DrawText("· " + line, new RectF(box.X + 26, ly, box.W - 52, lineH),
                    Color.Rgb(220, 228, 220), new TextStyle(16, Center: false, Family: Rounded));
                ly += lineH;
            }

            y = box.Bottom + 10;
        }

        _backButton = new RectF(Width / 2f - 90, Height - 80, 180, 44);
        c.FillRoundRect(_backButton, 10, Color.Rgb(40, 58, 48));
        c.StrokeRoundRect(_backButton, 10, Color.Rgb(214, 178, 92), 1.5f);
        c.DrawText("返回菜单", _backButton, Color.Rgb(242, 230, 190), new TextStyle(17, Bold: true, Center: true, Family: Rounded));
    }

    public override bool OnClick(float x, float y)
    {
        if (_backButton.Contains(x, y))
        {
            _back();
            return true;
        }

        return false;
    }

    public override bool OnPointerMove(float x, float y) => _backButton.Contains(x, y);

    public override bool OnKey(Keys key)
    {
        if (key is Keys.Escape or Keys.Enter or Keys.Space)
        {
            _back();
            return true;
        }

        return false;
    }
}
