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
    private readonly SettlementOverlay _overlay = new();
    private readonly ActionBar _actionBar = new();
    private readonly Func<TableViewModel> _snapshot;
    private readonly Action<GameCommand>? _commands;
    private readonly Action? _backToMenu;
    private RoundResult? _settlement;
    private int _hover = -1;
    private int _lastHandCount = -1;
    private int _lastDiscardTotal = -1;
    private int _lastMeldTotal = -1;
    private string _lastPhase = string.Empty;

    /// <summary>演示快照（<c>--render-table</c> / <c>--render-screen table</c> 用）。</summary>
    public TableScreen(TileAtlas? atlas)
        : this(() => TableDemoModel.Create(1), null)
    {
    }

    /// <summary>正式牌桌：只读会话快照 + 投递命令。</summary>
    public TableScreen(GameSession session, TileAtlas? atlas, Action? backToMenu = null)
        : this(() => session.LatestSnapshot, session.Post, backToMenu)
    {
        session.UiEvent += OnSessionEvent;
    }

    /// <summary>自检 / 预览用：直接给快照与命令通道（不订阅会话事件）。</summary>
    public TableScreen(Func<TableViewModel> snapshot, Action<GameCommand>? commands, Action? backToMenu = null)
        : base("table")
    {
        _snapshot = snapshot;
        _commands = commands;
        _backToMenu = backToMenu;
        YogaTableLayout.Apply(_layout);
    }

    /// <summary>无窗口自检：直接把一份局末结果交给结算层。</summary>
    public void ShowSettlement(RoundResult result) => _settlement = result;

    private void OnSessionEvent(UiEvent uiEvent)
    {
        switch (uiEvent)
        {
            case RoundEndEvent end:
                _settlement = end.Result;
                break;
            case StateChangedEvent changed when changed.Snapshot.Phase is not ("round_end" or "match_over"):
                _settlement = null;
                break;
        }
    }

    public override float CanvasWidth => _layout.CanvasW;

    public override float CanvasHeight => _layout.CanvasH;

    public override Color Background => Color.Rgb(20, 30, 24);

    public override void Draw(ICanvas c, double time)
    {
        var vm = _snapshot();
        _scene.Draw(c, vm, _layout, _hover);
        if (vm.Phase is "round_end" or "match_over" && _settlement is { } settlement)
        {
            _overlay.Draw(c, settlement, vm);
            return;
        }

        _actionBar.Build(vm, _layout);
        _actionBar.DrawTenpai(c, vm, _layout);
        _actionBar.Draw(c);
        PlayCues(vm);
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

    }

    private static string ActionLine(TableViewModel vm)
    {
        if (vm.Phase == "match_over")
        {
            return "整场结束 · R / Enter 返回菜单";
        }

        if (vm.Phase == "round_end")
        {
            return "本局结束 · Enter 下一局 · Esc 回菜单";
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
        if (_snapshot().Phase is "round_end" or "match_over")
        {
            _hover = -1;
            return false;
        }

        if (_actionBar.HitTest(x, y) is not null)
        {
            _hover = -1;
            return true;
        }

        _hover = HitTest(x, y);
        return _hover >= 0;
    }

    public override bool OnClick(float x, float y)
    {
        if (_snapshot().Phase is "round_end" or "match_over")
        {
            return false;
        }

        var button = _actionBar.HitTest(x, y);
        if (button is not null)
        {
            ActivateButton(button, _snapshot());
            return true;
        }

        int index = HitTest(x, y);
        if (index < 0 || _commands is null)
        {
            return index >= 0;
        }

        _commands(GameCommand.Discard(index));
        return true;
    }

    /// <summary>按快照差异播报音效（不做精确事件对齐，够用且不会漏）。</summary>
    private void PlayCues(TableViewModel vm)
    {
        if (vm.Seats.Count == 0)
        {
            return;
        }

        int hand = vm.Seats[0].Hand.Count;
        int discards = 0;
        int melds = 0;
        foreach (var seat in vm.Seats)
        {
            discards += seat.Discards.Count;
            melds += seat.Melds.Count;
        }

        if (_lastHandCount >= 0 && hand > _lastHandCount)
        {
            SoundEffects.Play(SoundEffects.Cue.Draw);
        }

        if (_lastDiscardTotal >= 0 && discards > _lastDiscardTotal)
        {
            SoundEffects.Play(SoundEffects.Cue.Discard);
        }

        if (_lastMeldTotal >= 0 && melds > _lastMeldTotal)
        {
            SoundEffects.Play(SoundEffects.Cue.Call);
        }

        if (_lastPhase != vm.Phase && vm.Phase is "round_end" or "match_over")
        {
            SoundEffects.Play(_settlement is { Kind: "win" or "tsumo" or "ron" }
                ? SoundEffects.Cue.Win
                : SoundEffects.Cue.Flow);
        }

        _lastHandCount = hand;
        _lastDiscardTotal = discards;
        _lastMeldTotal = melds;
        _lastPhase = vm.Phase;
    }

    /// <summary>按钮点击 → 命令（与键盘快捷键同一条路径）。</summary>
    private void ActivateButton(ActionBar.Button b, TableViewModel vm)
    {
        if (b.Kind == ActionBar.Kind.Tenpai)
        {
            _actionBar.ToggleTenpai();
            return;
        }

        if (_commands is null)
        {
            return;
        }

        switch (b.Kind)
        {
            case ActionBar.Kind.Kan:
                if (vm.Phase == "human_meld")
                {
                    _commands(GameCommand.KanMinkan());
                }
                else
                {
                    _commands(vm.HasAnkan ? GameCommand.KanAn() : GameCommand.KanKakan());
                }

                break;
            case ActionBar.Kind.Tsumo:
                _commands(GameCommand.Tsumo());
                break;
            case ActionBar.Kind.Ron:
                _commands(GameCommand.Ron());
                break;
            case ActionBar.Kind.Pon:
                _commands(GameCommand.Pon());
                break;
            case ActionBar.Kind.Chi:
                _commands(GameCommand.Chi(b.Option));
                break;
            case ActionBar.Kind.Pass:
                _commands(GameCommand.Pass());
                break;
            case ActionBar.Kind.Flow:
                _commands(GameCommand.Flow());
                break;
        }
    }

    public override bool OnKey(Keys key)
    {
        if (key == Keys.M)
        {
            return true;
        }

        var vm = _snapshot();

        // 局末 / 终局：面板外点击无效，这里只认键盘。
        if (vm.Phase == "match_over")
        {
            if (key is Keys.R or Keys.Enter)
            {
                _backToMenu?.Invoke();
                return true;
            }

            return false;
        }

        if (vm.Phase == "round_end")
        {
            if (key is Keys.Enter or Keys.Space or Keys.N)
            {
                _commands?.Invoke(GameCommand.NextRound());
                return true;
            }

            if (key == Keys.Escape)
            {
                _backToMenu?.Invoke();
                return true;
            }

            return false;
        }

        if (_commands is null)
        {
            return false;
        }

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
