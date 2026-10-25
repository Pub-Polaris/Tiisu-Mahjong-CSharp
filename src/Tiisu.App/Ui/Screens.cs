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
    private readonly LayoutEditor _editor;
    private readonly Func<TableViewModel> _snapshot;
    private readonly Action<GameCommand>? _commands;
    private readonly Action? _backToMenu;
    /// <summary>结算覆盖层：左侧直接画和牌者手牌，右侧只给结果；多人和牌可翻页。</summary>
    private RoundResult? _settlement;
    private bool _settlementHidden;

    private int _hover = -1;
    private int _lastHandCount = -1;
    private int _lastDiscardTotal = -1;
    private int _lastMeldTotal = -1;
    private string _lastPhase = string.Empty;
    private int _lastTenpaiCount;
    private string _lastRoundLabel = string.Empty;

    /// <summary>报听待弃状态：点「报听」后，下一张手牌 = 打出并完成报听。</summary>
    private bool _callArmed;

    /// <summary>长考秒数（0 = 关闭）；到点自动摸切 / 自动过吃碰杠。</summary>
    private readonly int _thinkSeconds;
    private DateTime _humanPhaseSince;
    private bool _humanPhaseTracked;
    private double _thinkLeft = -1;

    /// <summary>牌山查看器：按住右下角按钮时显示，松开恢复牌桌。</summary>
    private bool _wallHeld;
    private readonly bool _showWallButton;

    private RectF WallButtonRect => new(CanvasWidth - 176, CanvasHeight - 108, 156, 48);

    /// <summary>演示快照（<c>--render-table</c> / <c>--render-screen table</c> 用）。</summary>
    public TableScreen(TileAtlas? atlas)
        : this(() => TableDemoModel.Create(1), null, null, atlas)
    {
    }

    /// <summary>演示快照 + 指定观感（<c>--render-25d</c> 用）。</summary>
    public TableScreen(TileAtlas? atlas, TableRenderStyle renderStyle)
        : this(() => TableDemoModel.Create(1), null, null, atlas, renderStyle)
    {
    }

    /// <summary>正式牌桌：只读会话快照 + 投递命令。</summary>
    public TableScreen(GameSession session, TileAtlas? atlas, Action? backToMenu = null,
        TableRenderStyle renderStyle = TableRenderStyle.Mixed, Settings? settings = null)
        : this(() => session.LatestSnapshot, session.Post, backToMenu, atlas, renderStyle, settings)
    {
        session.UiEvent += OnSessionEvent;
    }

    /// <summary>自检 / 预览用：直接给快照与命令通道（不订阅会话事件）。</summary>
    public TableScreen(Func<TableViewModel> snapshot, Action<GameCommand>? commands, Action? backToMenu = null,
        TileAtlas? atlas = null, TableRenderStyle renderStyle = TableRenderStyle.Mixed, Settings? settings = null)
        : base("table")
    {
        _snapshot = snapshot;
        _commands = commands;
        _backToMenu = backToMenu;
        _thinkSeconds = Math.Max(0, settings?.ThinkSeconds ?? 0);
        _showWallButton = settings?.ShowWallViewer ?? false;
        _scene.Style = renderStyle;

        // Mixed 的透视桌面缩进画布：内容整体内收，四边留安全边距（手牌不再贴/出窗口底边）。
        // Flat / Light / Medium 不调用，保持原来的 16px 布局。
        if (renderStyle == TableRenderStyle.Mixed)
        {
            _layout.ApplyMixedSafeArea();
        }

        YogaTableLayout.Apply(_layout);

        // 启动时应用自定义布局（文件不存在 / 损坏 → 保持默认，不崩）。
        CustomLayoutStore.Load(_layout);
        _editor = new LayoutEditor(_layout);
    }

    /// <summary>长考：人类回合倒计时，到点自动摸切（打刚摸到的牌）/ 自动过吃碰杠。</summary>
    public override void Update(double dt)
    {
        base.Update(dt);
        var vm = _snapshot();
        bool human = vm.Phase is "human_discard" or "human_meld" or "human_ron";
        if (!human)
        {
            _humanPhaseTracked = false;
            _thinkLeft = -1;
            return;
        }

        if (!_humanPhaseTracked)
        {
            _humanPhaseTracked = true;
            _humanPhaseSince = DateTime.UtcNow;
        }

        if (_thinkSeconds <= 0)
        {
            _thinkLeft = -1;
            return;
        }

        double left = _thinkSeconds - (DateTime.UtcNow - _humanPhaseSince).TotalSeconds;
        _thinkLeft = Math.Max(0, left);
        if (left <= 0)
        {
            _humanPhaseTracked = false;
            _thinkLeft = -1;
            AutoAct(vm);
        }
    }

    private void AutoAct(TableViewModel vm)
    {
        if (vm.Phase is "human_meld" or "human_ron")
        {
            _commands?.Invoke(GameCommand.Pass());
            return;
        }

        if (vm.Phase != "human_discard" || vm.Seats.Count == 0)
        {
            return;
        }

        var hand = vm.Seats[0].Hand;
        int idx = -1;
        if (!string.IsNullOrEmpty(vm.DrawnTileId))
        {
            for (int i = hand.Count - 1; i >= 0; i--)
            {
                if (hand[i].Id == vm.DrawnTileId)
                {
                    idx = i;
                    break;
                }
            }
        }

        if (idx < 0)
        {
            idx = hand.Count - 1;
        }

        if (idx >= 0)
        {
            _commands?.Invoke(GameCommand.Discard(idx));
        }
    }

    /// <summary>右下角「骰子」按钮：按住显示本局的定庄骰 / 切牌骰与开局位，松开立刻回牌桌（不显示牌山，避免作弊）。</summary>
    private void DrawDiceViewer(ICanvas c, TableViewModel vm)
    {
        if (!_showWallButton)
        {
            return;
        }

        var btn = WallButtonRect;
        c.FillRoundRect(btn, 10, Color.Rgb(28, 40, 36).WithAlpha((byte)(_wallHeld ? 245 : 200)));
        c.StrokeRoundRect(btn, 10, _wallHeld ? Color.Rgb(242, 201, 76) : Color.Rgb(90, 110, 98), 1.2f);
        c.DrawText("骰子", new RectF(btn.X, btn.Y + 4, btn.W, 22), Color.Rgb(226, 236, 228),
            new TextStyle(15, Bold: true, Center: true));
        c.DrawText("按住查看", new RectF(btn.X, btn.Y + 26, btn.W, 18), Color.Rgb(150, 168, 158),
            new TextStyle(11, Center: true));

        if (!_wallHeld)
        {
            return;
        }

        var panel = new RectF(220, CanvasHeight / 2f - 110, CanvasWidth - 440, 220);
        c.FillRoundRect(panel, 14, Color.Black.WithAlpha((byte)236));
        c.StrokeRoundRect(panel, 14, Color.Rgb(242, 201, 76), 1.4f);

        float x = panel.X + 26;
        float width = panel.W - 52;
        float y = panel.Y + 22;
        c.DrawText("本局开局", new RectF(x, y, width, 26), Color.Rgb(242, 201, 76),
            new TextStyle(18, Bold: true, Center: false));
        y += 40;
        c.DrawText(vm.DiceDealerText ?? "定庄骰：（首局之后沿用上局庄家，不再掷）",
            new RectF(x, y, width, 24), Color.Rgb(226, 236, 228), new TextStyle(16, Center: false));
        y += 34;
        c.DrawText(vm.DiceCutText ?? "切牌骰：—",
            new RectF(x, y, width, 24), Color.Rgb(226, 236, 228), new TextStyle(16, Center: false));
        y += 34;
        c.DrawText("松开恢复牌桌", new RectF(x, y, width, 22), Color.Rgb(150, 168, 158),
            new TextStyle(12, Center: false));
    }
    /// <summary>布局编辑器（F2）。自检 / 出图直接驱动它。</summary>
    public LayoutEditor Editor => _editor;

    /// <summary>从菜单按 F2 进来：退出编辑器时回菜单，不把玩家丢在空牌桌上。</summary>
    private bool _returnToMenuAfterEditor;

    /// <summary>菜单里按 F2 的入口：切到牌桌并直接进入布局编辑态。</summary>
    public void OpenEditorForLayout()
    {
        _returnToMenuAfterEditor = true;
        _editor.Enter();
    }

    private void LeaveEditorIfTemporary()
    {
        if (!_returnToMenuAfterEditor)
        {
            return;
        }

        _returnToMenuAfterEditor = false;
        _backToMenu?.Invoke();
    }

    /// <summary>结算覆盖层（自检用：翻页按钮 / 当前和牌者）。</summary>
    internal SettlementOverlay SettlementOverlay => _overlay;

    /// <summary>无窗口自检：直接把一份局末结果交给结算层。</summary>
    public void ShowSettlement(RoundResult result)
    {
        _settlement = result;
        _settlementHidden = false;
    }

    private void OnSessionEvent(UiEvent uiEvent)
    {
        switch (uiEvent)
        {
            case RoundEndEvent end:
                _settlement = end.Result;
                _settlementHidden = false;
                break;
            case StateChangedEvent changed when changed.Snapshot.Phase is not ("round_end" or "match_over"):
                _settlement = null;
                _settlementHidden = false;
                break;
        }
    }

    public override float CanvasWidth => _layout.CanvasW;

    public override float CanvasHeight => _layout.CanvasH;

    public override Color Background => Color.Rgb(20, 30, 24);

    public override void Draw(ICanvas c, double time)
    {
        var vm = _snapshot();
        if (vm.Phase is "round_end" or "match_over" && _settlement is { } settlement && !_settlementHidden)
        {
            // 结算层直接用牌面画和牌者的手牌（不再渲染牌桌缩略图）。
            _overlay.Draw(c, settlement, vm, _layout);
            _editor.Draw(c);
            return;
        }

        _scene.Draw(c, vm, _layout, _hover);
        if (_callArmed && vm.Phase != "human_discard")
        {
            _callArmed = false;
        }

        _actionBar.Build(vm, _layout);
        _actionBar.DrawTenpai(c, vm, _layout);
        _actionBar.Draw(c);

        // 【错和可能】：牌形听牌但所有待牌赋数不足 → 报和必错和，提醒玩家。
        if (vm.CallWouldMisfire)
        {
            var bar = _layout.ActionBarRect;
            var hint = new RectF(bar.CenterX - 190, bar.Y - 30, 380, 24);
            c.DrawText("【错和可能】待牌赋数不足，报和会被判错和",
                hint, Color.Rgb(255, 176, 64), new TextStyle(16, Bold: true, Center: true));
        }

        PlayCues(vm);
        DrawHud(c, vm);

        // 长考倒计时（设置里配了秒数才显示）。
        if (_thinkLeft >= 0 && _thinkSeconds > 0)
        {
            var bar = _layout.ActionBarRect;
            c.DrawText("长考 " + ((int)Math.Ceiling(_thinkLeft)).ToString(CultureInfo.InvariantCulture) + "s",
                new RectF(bar.Right - 170, bar.Y - 30, 170, 24), Color.Rgb(200, 214, 202),
                new TextStyle(15, Center: false, Right: true));
        }

        DrawDiceViewer(c, vm);
        _editor.Draw(c);
    }

    /// <summary>左上角「菜单」按钮（点击即回菜单，不必依赖 ESC）。</summary>
    private RectF MenuButtonRect => new(_layout.HudRect.X + 6, _layout.HudRect.Y + 4, 96, _layout.HudRect.H - 8);

    private void DrawHud(ICanvas c, TableViewModel vm)
    {
        // 左上：可点击的「菜单」按钮 + 快捷键提示
        var btn = MenuButtonRect;
        c.FillRoundRect(btn, 9, Color.Rgb(28, 40, 36).WithAlpha(235));
        c.StrokeRoundRect(btn, 9, Color.Rgb(96, 122, 108), 1.3f);
        c.DrawText("菜单", btn, Color.Rgb(236, 242, 232), new TextStyle(18, Bold: true, Center: true));
        c.DrawText("M / ESC",
            new RectF(btn.Right + 10, btn.Y + 8, 90, 20),
            Color.Rgb(170, 186, 172), new TextStyle(14, Center: false));

        // 顶部中央：提示消息（和牌 / 流局 / 窗口提示）
        if (!string.IsNullOrEmpty(vm.Message))
        {
            var box = new RectF(_layout.CanvasW / 2f - 330, 18, 660, 38);
            c.FillRoundRect(box, 10, Color.Rgb(0, 0, 0).WithAlpha(150));
            c.DrawText(vm.Message, box, Color.Rgb(255, 228, 120), new TextStyle(17, Bold: true));
        }

        // 报听待弃：点一张手牌打出即完成报听（无二次确认）。
        if (_callArmed)
        {
            var box = new RectF(_layout.CanvasW / 2f - 300, 62, 600, 34);
            c.FillRoundRect(box, 10, Color.Rgb(96, 62, 12).WithAlpha(220));
            c.StrokeRoundRect(box, 10, Color.Rgb(255, 206, 120).WithAlpha(180), 1.2f);
            c.DrawText("报听：点一张手牌打出（再点「报听」取消）", box,
                Color.Rgb(255, 240, 200), new TextStyle(16, Bold: true));
        }

        // 已报听 / 禁和状态
        string status = vm.IsCalled ? "已报听" : vm.WinBanned ? "本局禁和" : string.Empty;
        if (status.Length > 0)
        {
            var box = new RectF(_layout.CanvasW - 190, 18, 166, 34);
            c.FillRoundRect(box, 10, Color.Rgb(0, 0, 0).WithAlpha(150));
            c.DrawText(status, box, vm.IsCalled ? Color.Rgb(255, 226, 140) : Color.Rgb(240, 150, 140),
                new TextStyle(16, Bold: true));
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
        if (_editor.Active)
        {
            _editor.PointerMove(x, y);
            return true;
        }

        if (_snapshot().Phase is "round_end" or "match_over")
        {
            _hover = -1;
            return _settlement is { } settled && !_settlementHidden && _overlay.SetPointer(x, y, settled);
        }

        if (_actionBar.HitTest(x, y) is not null)
        {
            _actionBar.SetPointer(x, y);
            _hover = -1;
            return true;
        }

        _actionBar.ClearPointer();
        _hover = HitTest(x, y);
        return _hover >= 0;
    }

    public override bool OnPointerDown(float x, float y)
    {
        if (_editor.Active)
        {
            return _editor.PointerDown(x, y);
        }

        if (WallButtonRect.Contains(x, y) && _showWallButton)
        {
            _wallHeld = true;
            return true;
        }

        return false;
    }

    public override bool OnPointerUp(float x, float y)
    {
        if (_wallHeld)
        {
            _wallHeld = false;
            return true;
        }

        if (!_editor.Active)
        {
            return false;
        }

        _editor.PointerUp();
        return true;
    }

    public override bool OnWheel(float x, float y, int delta, bool shift)
    {
        if (!_editor.Active || delta == 0)
        {
            return false;
        }

        // 滚轮上滚放大，下滚缩小；Shift 只改高度（都以左上角为锚）。
        _editor.ScaleBy(delta > 0 ? 1.1f : 1f / 1.1f, shift);
        return true;
    }

    public override bool OnClick(float x, float y)
    {
        if (_editor.Active)
        {
            return true;
        }

        // 左上角「菜单」按钮：点击回菜单（不必依赖 ESC）。
        if (_backToMenu is not null && MenuButtonRect.Contains(x, y))
        {
            _backToMenu();
            return true;
        }

        if (_snapshot().Phase is "round_end" or "match_over")
        {
            // 多人和牌：点右上角「下一家 ▶」翻到下一位的和牌详情。
            if (_settlement is { } settled && !_settlementHidden && _overlay.HitNextButton(x, y, settled))
            {
                _overlay.Step(settled, 1);
                return true;
            }

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

        if (_callArmed)
        {
            // 报听待弃：这张牌就是宣言时打出的牌。
            _callArmed = false;
            _commands(GameCommand.DeclareCall(index));
            return true;
        }

        _commands(GameCommand.Discard(index));
        return true;
    }

    /// <summary>副露类型全集（与引擎 <c>MeldType.Wire()</c> 一致），<c>--check-voices</c> 逐行核对用。</summary>
    public static readonly string[] MeldTypes = { "pon", "chi", "minkan", "ankan", "kakan" };

    /// <summary>副露类型 → 语音名：吃 / 碰 / 杠。暗杠、明杠、加杠都播「杠」。</summary>
    public static string VoiceForMeld(string type) => type switch
    {
        "ankan" or "minkan" or "kakan" => "gang",
        "chi" => "chi",
        _ => "peng",
    };

    /// <summary>最新一组副露 → 语音名（吃 / 碰 / 杠）。</summary>
    private static string NewestMeldVoice(TableViewModel vm)
    {
        foreach (var seat in vm.Seats)
        {
            if (seat.Melds.Count == 0)
            {
                continue;
            }

            return VoiceForMeld(seat.Melds[^1].Type);
        }

        return "peng";
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
            SoundEffects.PlayVoice(NewestMeldVoice(vm));
        }

        if (_lastPhase != vm.Phase && vm.Phase is "round_end" or "match_over")
        {
            bool won = _settlement is { Details.Count: > 0 };
            SoundEffects.Play(won ? SoundEffects.Cue.Win : SoundEffects.Cue.Flow);
            if (_settlement is { Details.Count: > 0 } settled)
            {
                SoundEffects.PlayVoice(settled.Details[0].SelfDraw ? "zimo" : "ronghe");
            }
            else
            {
                SoundEffects.PlayVoice("liuju");
            }
        }

        if (_lastRoundLabel.Length > 0 && !string.Equals(_lastRoundLabel, vm.RoundLabel, StringComparison.Ordinal)
            && vm.Phase is "human_discard" or "human_meld" or "human_ron")
        {
            SoundEffects.Play(SoundEffects.Cue.Deal);
        }

        _lastRoundLabel = vm.RoundLabel;

        if (_lastTenpaiCount == 0 && vm.TenpaiTiles.Count > 0)
        {
            SoundEffects.PlayVoice("tingpai");
        }

        _lastTenpaiCount = vm.TenpaiTiles.Count;

        _lastHandCount = hand;
        _lastDiscardTotal = discards;
        _lastMeldTotal = melds;
        _lastPhase = vm.Phase;
    }

    /// <summary>按钮点击 → 命令（与键盘快捷键同一条路径）。</summary>
    private void ActivateButton(ActionBar.Button b, TableViewModel vm)
    {
        if (b.Kind == ActionBar.Kind.DeclareCall)
        {
            BeginDeclareCall(vm);
            return;
        }

        if (!b.Enabled || _commands is null)
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

    /// <summary>
    /// 报听入口：出牌阶段先进入「待弃」态（下一张手牌打出即报听，再点一次取消）；
    /// 副露 / 荣和窗口立即宣言。真听牌之外（按钮灰显）不发命令。
    /// </summary>
    private void BeginDeclareCall(TableViewModel vm)
    {
        if (_commands is null || vm.IsCalled || (vm.Actions & HumanAction.DeclareCall) == 0)
        {
            return;
        }

        if (vm.Phase == "human_discard")
        {
            _callArmed = !_callArmed;
            return;
        }

        _commands(GameCommand.DeclareCall());
    }

    public override bool OnKey(Keys key) => HandleKey(key, false);

    public override bool OnKey(Keys key, bool shift) => HandleKey(key, shift);

    private bool HandleKey(Keys key, bool shift)
    {
        if (key == Keys.F2)
        {
            if (_editor.Active)
            {
                _editor.Exit();
                LeaveEditorIfTemporary();
            }
            else
            {
                _editor.Enter();
            }

            return true;
        }

        if (_editor.Active)
        {
            bool handled = _editor.OnKey(key, shift);
            if (!_editor.Active)
            {
                LeaveEditorIfTemporary();
            }

            return handled;
        }

        if (key == Keys.Escape)
        {
            return HandleEscape();
        }

        if (key == Keys.M)
        {
            return true;
        }

        var vm = _snapshot();

        // 局末 / 终局：面板外点击无效，这里只认键盘。
        if (vm.Phase == "match_over")
        {
            if (TryFlipWinner(key))
            {
                return true;
            }

            if (key is Keys.R or Keys.Enter)
            {
                _backToMenu?.Invoke();
                return true;
            }

            return false;
        }

        if (vm.Phase == "round_end")
        {
            if (TryFlipWinner(key))
            {
                return true;
            }

            if (key is Keys.Enter or Keys.Space or Keys.N)
            {
                _commands?.Invoke(GameCommand.NextRound());
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

            case Keys.T:
                if ((vm.Actions & HumanAction.DeclareCall) != 0 || vm.IsCalled)
                {
                    BeginDeclareCall(vm);
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

    /// <summary>多人和牌：← / → 翻页；只有一家或面板已关时返回 false（按键继续走后面的逻辑）。</summary>
    private bool TryFlipWinner(Keys key)
    {
        if (key is not (Keys.Left or Keys.Right) || _settlement is not { } settled || _settlementHidden)
        {
            return false;
        }

        return _overlay.Step(settled, key == Keys.Right ? 1 : -1);
    }

    /// <summary>
    /// ESC = 返回，不是退出程序：
    /// 结算弹窗先关掉看一眼牌桌；副露/荣和窗口先「过」；其余情况回菜单。
    /// </summary>
    private bool HandleEscape()
    {
        var vm = _snapshot();
        bool settled = (vm.Phase is "round_end" or "match_over") && _settlement is not null;

        if (settled && !_settlementHidden)
        {
            _settlementHidden = true;
            return true;
        }

        if (!settled && _commands is not null && (vm.Actions & HumanAction.Pass) != 0)
        {
            _commands(GameCommand.Pass());
            return true;
        }

        // 回菜单；再次进入牌桌时结算面板仍应可见。
        _settlementHidden = false;
        _backToMenu?.Invoke();
        return true;
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
    /// <summary>右下角作者署名（版本号由 <see cref="BuildInfo"/> 在构建时写入）。</summary>
    private const string Author = "公民北极星_Official·作";

    private readonly UiLayout _layout;
    private readonly Func<string, bool> _onAction;
    private readonly Action<string>? _openUrl;
    private readonly HashSet<string> _hover = new();
    private string _hoverLinkId = string.Empty;
    private int _hoverLinkIndex = -1;

    public MenuScreen(UiLayout layout, Func<string, bool> onAction, Action<string>? openUrl = null) : base("menu")
    {
        _layout = layout;
        _onAction = onAction;
        _openUrl = openUrl;

        // 版本行：布局 JSON 只负责位置，文案在构建时由 git 生成。
        foreach (var el in _layout.Elements)
        {
            if (string.Equals(el.Id, "buildInfo", StringComparison.Ordinal))
            {
                el.Text = BuildInfo.Version + " · " + Author;
            }
        }
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
                    DrawTextElement(c, el);
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

    /// <summary>
    /// 普通文字直接整段绘制；带 <see cref="LayoutElement.Links"/> 的按片段横排，
    /// 悬停片段换成亮色（只变色不加粗，避免宽度跳动导致点击区错位）。
    /// </summary>
    private void DrawTextElement(ICanvas c, LayoutElement el)
    {
        string align = (el.Align ?? "center").Trim().ToLowerInvariant();

        if (el.Links is { Count: > 0 })
        {
            var color = ParseColor(el.FontColor, Color.White);
            var hover = Color.Rgb(242, 230, 190);
            var rects = LinkRects(el);
            for (int i = 0; i < rects.Count; i++)
            {
                var (link, rect) = rects[i];
                bool lit = link.Url.Length > 0 && _hoverLinkId == el.Id && _hoverLinkIndex == i;
                c.DrawText(link.Text, rect, lit ? hover : color,
                    new TextStyle(el.FontSize, Center: false));
            }

            return;
        }

        c.DrawText(el.Text ?? string.Empty, el.Rect,
            ParseColor(el.FontColor, Color.White),
            new TextStyle(el.FontSize,
                Center: align != "left" && align != "right",
                Right: align == "right"));
    }

    /// <summary>署名片段各自的矩形（与绘制同一套测量，点击区=可见文字区）。</summary>
    public static IReadOnlyList<(LinkSegment Link, RectF Rect)> LinkRects(LayoutElement el)
    {
        var result = new List<(LinkSegment, RectF)>();
        if (el.Links is not { Count: > 0 })
        {
            return result;
        }

        var style = new TextStyle(el.FontSize, Center: false);
        float total = 0f;
        foreach (var link in el.Links)
        {
            total += SkiaCanvas.Measure(link.Text, style);
        }

        string align = (el.Align ?? "center").Trim().ToLowerInvariant();
        float x = align == "left" ? el.Rect.X
            : align == "right" ? el.Rect.Right - total
            : el.Rect.CenterX - (total / 2f);

        foreach (var link in el.Links)
        {
            float w = SkiaCanvas.Measure(link.Text, style);
            result.Add((link, new RectF(x, el.Rect.Y, w, el.Rect.H)));
            x += w;
        }

        return result;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _hover.Clear();
        _hoverLinkId = string.Empty;
        _hoverLinkIndex = -1;
        bool overLink = false;

        foreach (var el in _layout.Elements)
        {
            if (el.Links is { Count: > 0 } && el.Rect.Contains(x, y))
            {
                var rects = LinkRects(el);
                for (int i = 0; i < rects.Count; i++)
                {
                    if (rects[i].Link.Url.Length > 0 && rects[i].Rect.Contains(x, y))
                    {
                        _hoverLinkId = el.Id;
                        _hoverLinkIndex = i;
                        overLink = true;
                    }
                }
            }

            if (el.Type == "button" && el.Rect.Contains(x, y))
            {
                _hover.Add(el.Id);
            }
        }

        return overLink || _hover.Count > 0;
    }

    public override bool OnClick(float x, float y)
    {
        foreach (var el in _layout.Elements)
        {
            // 署名链接优先于按钮：两者不重叠，顺序只是让 URL 片段先命中。
            if (el.Links is { Count: > 0 } && el.Rect.Contains(x, y))
            {
                foreach (var (link, rect) in LinkRects(el))
                {
                    if (link.Url.Length > 0 && rect.Contains(x, y))
                    {
                        _openUrl?.Invoke(link.Url);
                        return true;
                    }
                }
            }

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
