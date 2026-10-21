using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 牌桌屏：1200×1200 方形画布，布局由 Yoga 计算，绘制走 TableSceneSkia。
/// 数据来自 <see cref="GameSession.LatestSnapshot"/>（不可变快照）；点击手牌投递 <see cref="GameCommand.Discard"/>。
/// 不带会话时（<c>--render-*</c> 预览）退回演示快照。
/// </summary>
public sealed class TableScreen : Screen
{
    private readonly TableLayoutSpec _layout = new() { CanvasW = 1200f, CanvasH = 1200f };
    private readonly TableSceneSkia _scene = new();
    private readonly Func<TableViewModel> _snapshot;
    private readonly Action<GameCommand>? _commands;
    private int _hover = -1;

    /// <summary>演示快照（<c>--render-table</c> / <c>--render-screen table</c> 用）。</summary>
    public TableScreen(TileAtlas? atlas)
        : this(() => TableDemoModel.Create(1), null)
    {
    }

    /// <summary>正式牌桌：只读会话快照 + 投递命令。</summary>
    public TableScreen(GameSession session, TileAtlas? atlas)
        : this(() => session.LatestSnapshot, session.Post)
    {
    }

    private TableScreen(Func<TableViewModel> snapshot, Action<GameCommand>? commands) : base("table")
    {
        _snapshot = snapshot;
        _commands = commands;
        YogaTableLayout.Apply(_layout);
    }

    public override float CanvasWidth => _layout.CanvasW;

    public override float CanvasHeight => _layout.CanvasH;

    public override Color Background => Color.Rgb(20, 30, 24);

    public override void Draw(ICanvas c, double time)
    {
        var vm = _snapshot();
        _scene.Draw(c, vm, _layout, _hover);
        DrawHud(c, vm);
    }

    private void DrawHud(ICanvas c, TableViewModel vm)
    {
        // 左上：快捷键
        var hint = new RectF(24, 20, 300, 34);
        c.FillRoundRect(hint, 8, Color.Rgb(0, 0, 0).WithAlpha(130));
        c.DrawText("M：菜单    ESC：退出",
            new RectF(hint.X + 12, hint.Y + 8, hint.W - 16, 20),
            Color.Rgb(230, 236, 226), new TextStyle(15, Center: false));

        // 顶部中央：提示消息（和牌 / 流局 / 窗口提示）
        if (!string.IsNullOrEmpty(vm.Message))
        {
            var box = new RectF(_layout.CanvasW / 2f - 330, 18, 660, 38);
            c.FillRoundRect(box, 10, Color.Rgb(0, 0, 0).WithAlpha(150));
            c.DrawText(vm.Message, box, Color.Rgb(255, 228, 120), new TextStyle(17, Bold: true));
        }

        // 手牌上方：可用动作 / 局末提示
        string line = ActionLine(vm);
        if (line.Length == 0)
        {
            return;
        }

        var hand = _layout.HandRects[0];
        var bar = new RectF(hand.X, hand.Y - 44, hand.W, 34);
        c.FillRoundRect(bar, 8, Color.Rgb(0, 0, 0).WithAlpha(140));
        c.DrawText(line, bar, Color.Rgb(255, 236, 180), new TextStyle(16, Bold: true));
    }

    private static string ActionLine(TableViewModel vm)
    {
        if (vm.Phase == "match_over")
        {
            return "整场结束 · M 返回菜单";
        }

        if (vm.Phase == "round_end")
        {
            return "本局结束 · N 下一局";
        }

        var parts = new List<string>();

        // 按钮优先级（与 Web 侧一致）：杠 > 和 > 碰 > 吃 > 过
        if ((vm.Actions & HumanAction.Kan) != 0)
        {
            parts.Add("K 杠");
        }

        if ((vm.Actions & HumanAction.Tsumo) != 0)
        {
            parts.Add("H 和牌");
        }
        else if ((vm.Actions & HumanAction.Ron) != 0)
        {
            parts.Add("H 荣和");
        }

        if ((vm.Actions & HumanAction.Pon) != 0)
        {
            parts.Add("P 碰");
        }

        if ((vm.Actions & HumanAction.Chi) != 0)
        {
            parts.Add(vm.ActionLabels.Count > 1 ? "C 吃(多组)" : "C 吃");
        }

        if ((vm.Actions & HumanAction.Pass) != 0)
        {
            parts.Add("Space 过");
        }

        if ((vm.Actions & HumanAction.Flow) != 0)
        {
            parts.Add("F 流局");
        }

        if ((vm.Actions & HumanAction.Discard) != 0)
        {
            parts.Add("点手牌出牌");
        }

        return string.Join("   ", parts);
    }

    public override bool OnPointerMove(float x, float y)
    {
        _hover = HitTest(x, y);
        return _hover >= 0;
    }

    public override bool OnClick(float x, float y)
    {
        int index = HitTest(x, y);
        if (index < 0 || _commands is null)
        {
            return index >= 0;
        }

        _commands(GameCommand.Discard(index));
        return true;
    }

    public override bool OnKey(Keys key)
    {
        if (key == Keys.M)
        {
            return true;
        }

        if (_commands is null)
        {
            return false;
        }

        var vm = _snapshot();
        switch (key)
        {
            case Keys.H:
                if ((vm.Actions & HumanAction.Tsumo) != 0)
                {
                    _commands(GameCommand.Tsumo());
                    return true;
                }

                if ((vm.Actions & HumanAction.Ron) != 0)
                {
                    _commands(GameCommand.Ron());
                    return true;
                }

                return false;

            case Keys.P:
                if ((vm.Actions & HumanAction.Pon) != 0)
                {
                    _commands(GameCommand.Pon());
                    return true;
                }

                return false;

            case Keys.C:
                if ((vm.Actions & HumanAction.Chi) != 0)
                {
                    _commands(GameCommand.Chi(0));
                    return true;
                }

                return false;

            case Keys.K:
                if (vm.Phase == "human_meld" && (vm.Actions & HumanAction.Kan) != 0)
                {
                    _commands(GameCommand.KanMinkan());
                    return true;
                }

                if (vm.Phase == "human_discard" && (vm.Actions & HumanAction.Kan) != 0)
                {
                    _commands(vm.HasAnkan ? GameCommand.KanAn() : GameCommand.KanKakan());
                    return true;
                }

                return false;

            case Keys.Space:
                if ((vm.Actions & HumanAction.Pass) != 0)
                {
                    _commands(GameCommand.Pass());
                    return true;
                }

                return false;

            case Keys.F:
                if ((vm.Actions & HumanAction.Flow) != 0)
                {
                    _commands(GameCommand.Flow());
                    return true;
                }

                return false;

            case Keys.N:
                if (vm.Phase == "round_end")
                {
                    _commands(GameCommand.NextRound());
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>手牌命中：与绘制共用同一份矩形算式。</summary>
    private int HitTest(float x, float y)
    {
        var vm = _snapshot();
        if (vm.Seats.Count == 0)
        {
            return -1;
        }

        var self = vm.Seats[0];
        if ((vm.Actions & HumanAction.Discard) == 0 || self.Hand.Count == 0)
        {
            return -1;
        }

        var rects = TableSceneSkia.HandTileRects(_layout, self, 0);
        int count = Math.Min(rects.Length, self.Hand.Count);
        for (int i = 0; i < count; i++)
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
