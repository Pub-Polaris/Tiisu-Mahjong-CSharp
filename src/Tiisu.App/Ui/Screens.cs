using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>牌桌屏：复用 BoardScene（牌桌/手牌/牌河/旋转牌），叠加左上角菜单按钮提示。</summary>
public sealed class TableScreen : Screen
{
    private readonly BoardScene _scene;
    private int _hover;

    public TableScreen(TileAtlas? atlas) : base("table")
    {
        _scene = new BoardScene(atlas);
    }

    public BoardScene Scene => _scene;

    public override void Draw(ICanvas c, double time)
    {
        _scene.Draw(c, time, _hover);

        var hint = new RectF(24, 24, 220, 40);
        c.FillRoundRect(hint, 8, Color.Rgb(0, 0, 0).WithAlpha(120));
        c.DrawText("M：菜单    ESC：退出", new RectF(hint.X, hint.Y, hint.W, hint.H),
            Color.Rgb(230, 236, 226), new TextStyle(16, Center: true));
    }

    public override bool OnPointerMove(float x, float y)
    {
        int hit = BoardScene.HitTestHand(x, y);
        _hover = hit;
        return hit >= 0;
    }

    public override bool OnClick(float x, float y) => BoardScene.HitTestHand(x, y) >= 0;

    public override bool OnKey(Keys key) => key == Keys.M;
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
