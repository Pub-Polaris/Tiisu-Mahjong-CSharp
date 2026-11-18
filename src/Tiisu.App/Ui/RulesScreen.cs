using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.Localization;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>菜单「规则速查」：只讲七子特有规则，不写完整麻将教程。</summary>
public sealed class RulesScreen : Screen
{
    private readonly Action _back;
    private readonly List<(string Title, string[] Lines)> _sections = new()
    {
        ("app.ui.rules.section1.title", new[]
        {
            "app.ui.rules.section1.line1",
            "app.ui.rules.section1.line2",
            "app.ui.rules.section1.line3",
        }),
        ("app.ui.rules.section2.title", new[]
        {
            "app.ui.rules.section2.line1",
            "app.ui.rules.section2.line2",
            "app.ui.rules.section2.line3",
        }),
        ("app.ui.rules.section3.title", new[]
        {
            "app.ui.rules.section3.line1",
            "app.ui.rules.section3.line2",
            "app.ui.rules.section3.line3",
        }),
    };

    private const string Rounded = "Tiisu Rounded SC";

    private RectF _backButton;

    public RulesScreen(Action back) : base("rules")
    {
        _back = back;
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText(L.T("app.ui.rules.title"), new RectF(0, 54, Width, 56),
            Color.Rgb(242, 230, 190), new TextStyle(42, Bold: true, Center: true, Family: Rounded));
        c.DrawText(L.T("app.ui.rules.subtitle"), new RectF(0, 108, Width, 26),
            Color.Rgb(159, 180, 170), new TextStyle(16, Center: true, Family: Rounded));

        float y = 136;
        const float lineH = 32f;
        foreach (var (title, lines) in _sections)
        {
            float boxH = 14f + 28f + lines.Length * lineH + 10f;
            var box = new RectF(150, y, Width - 300, boxH);
            c.FillRoundRect(box, 14, Color.Rgb(26, 38, 34));
            c.StrokeRoundRect(box, 14, Color.Rgb(96, 122, 108), 1.2f);
            c.DrawText(L.T(title), new RectF(box.X + 26, box.Y + 10, box.W - 52, 28),
                Color.Rgb(242, 201, 76), new TextStyle(20, Bold: true, Center: false, Family: Rounded));
            float ly = box.Y + 42;
            foreach (string line in lines)
            {
                c.DrawText("· " + L.T(line), new RectF(box.X + 26, ly, box.W - 52, lineH),
                    Color.Rgb(220, 228, 220), new TextStyle(16, Center: false, Family: Rounded));
                ly += lineH;
            }

            y = box.Bottom + 10;
        }

        _backButton = new RectF(Width / 2f - 90, Height - 80, 180, 44);
        c.FillRoundRect(_backButton, 10, Color.Rgb(40, 58, 48));
        c.StrokeRoundRect(_backButton, 10, Color.Rgb(214, 178, 92), 1.5f);
        c.DrawText(L.T("app.ui.rules.back"), _backButton, Color.Rgb(242, 230, 190), new TextStyle(17, Bold: true, Center: true, Family: Rounded));
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
