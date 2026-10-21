using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Table;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>牌桌屏：1200×1200 方形画布，布局由 Yoga 计算，绘制走 TableSceneSkia。</summary>
public sealed class TableScreen : Screen
{
    private readonly TableLayoutSpec _layout = new() { CanvasW = 1200f, CanvasH = 1200f };
    private readonly TableViewModel _vm = TableDemoModel.Create(0);
    private readonly TableSceneSkia _scene = new();
    private int _hover = -1;

    public TableScreen(TileAtlas? atlas) : base("table")
    {
        YogaTableLayout.Apply(_layout);
    }

    public override float CanvasWidth => _layout.CanvasW;

    public override float CanvasHeight => _layout.CanvasH;

    public override Color Background => Color.Rgb(20, 30, 24);

    public override void Draw(ICanvas c, double time)
    {
        _scene.Draw(c, _vm, _layout, _hover);

        var hint = new RectF(24, 24, 220, 40);
        c.FillRoundRect(hint, 8, Color.Rgb(0, 0, 0).WithAlpha(120));
        c.DrawText("M：菜单    ESC：退出", hint, Color.Rgb(230, 236, 226), new TextStyle(16));
    }

    public override bool OnPointerMove(float x, float y)
    {
        _hover = HitTest(x, y);
        return _hover >= 0;
    }

    public override bool OnClick(float x, float y) => HitTest(x, y) >= 0;

    public override bool OnKey(Keys key) => key == Keys.M;

    /// <summary>手牌命中：与绘制共用同一份矩形算式。</summary>
    private int HitTest(float x, float y)
    {
        var rects = TableSceneSkia.HandTileRects(_layout, _vm.Seats[0], 0);
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i].Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>菜单屏：完全由布局 JSON 驱动（菜单项 → action）。</summary>
public sealed class MenuScreen : Screen
{
    private readonly UiLayout _layout;
    private readonly Func<string, bool> _onAction;
    private readonly HashSet<string> _hover = new();

    public MenuScreen(UiLayout layout, Func<string, bool> onAction) : base("menu")
    {
        _layout = layout;
        _onAction = onAction;
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(20, 28, 34), Color.Rgb(8, 12, 16));

        foreach (var el in _layout.Elements)
        {
            switch (el.Type)
            {
                case "rect":
                    c.FillRoundRect(el.Rect, el.Radius, ParseColor(el.Fill, Color.Rgb(30, 40, 46)));
                    if (!string.IsNullOrEmpty(el.Border))
                    {
                        c.StrokeRoundRect(el.Rect, el.Radius, ParseColor(el.Border, Color.White), 1.5f);
                    }

                    break;

                case "text":
                    c.DrawText(el.Text ?? string.Empty, el.Rect,
                        ParseColor(el.FontColor, Color.White), new TextStyle(el.FontSize, Center: true));
                    break;

                case "tile":
                    if (!string.IsNullOrEmpty(el.TileId))
                    {
                        c.DrawTile(el.TileId, el.Rect);
                    }

                    break;

                case "button":
                    bool isHover = _hover.Contains(el.Id);
                    var fill = isHover ? Color.Rgb(52, 86, 78) : ParseColor(el.Fill, Color.Rgb(34, 52, 56));
                    c.FillRoundRect(el.Rect, el.Radius <= 0 ? 10 : el.Radius, fill);
                    c.StrokeRoundRect(el.Rect, el.Radius <= 0 ? 10 : el.Radius,
                        ParseColor(el.Border, Color.Rgb(196, 168, 96)), 1.5f);
                    c.DrawText(el.Text ?? el.Id, el.Rect,
                        ParseColor(el.FontColor, Color.Rgb(240, 236, 220)),
                        new TextStyle(el.FontSize, Bold: true, Center: true));
                    break;
            }
        }
    }

    public override bool OnPointerMove(float x, float y)
    {
        _hover.Clear();
        foreach (var el in _layout.Elements)
        {
            if (el.Type == "button" && el.Rect.Contains(x, y))
            {
                _hover.Add(el.Id);
            }
        }

        return _hover.Count > 0;
    }

    public override bool OnClick(float x, float y)
    {
        foreach (var el in _layout.Elements)
        {
            if (el.Type == "button" && el.Rect.Contains(x, y) && !string.IsNullOrEmpty(el.Action))
            {
                return _onAction(el.Action!);
            }
        }

        return false;
    }

    public override bool OnKey(Keys key) => key == Keys.M || key == Keys.Escape;

    private static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return fallback;
        }

        string s = hex.TrimStart('#');
        if (s.Length != 6 && s.Length != 8)
        {
            return fallback;
        }

        byte r = byte.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte a = s.Length == 8
            ? byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : (byte)255;
        return new Color(r, g, b, a);
    }
}
