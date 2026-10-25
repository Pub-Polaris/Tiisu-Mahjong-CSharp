using System;
using System.Collections.Generic;
using Tiisu.App.Rendering;
using Tiisu.App.Table;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 手牌上方的动作按钮条：杠 / 和 / 碰 / 吃（多组各一颗）/ 过 / 流局 + 报听（叫胡）。
/// 点击返回对应命令；键盘快捷键仍可用（H/P/C/K/Space/F/T）。
/// <para>报听按钮三态：见 <see cref="TableViewModel.IsCalled"/>（已报听）/ <see cref="HumanAction.DeclareCall"/>
/// （真听牌，可点）/ 其余（灰显不可点）。</para>
/// </summary>
public sealed class ActionBar
{
    public enum Kind
    {
        Kan,
        Tsumo,
        Ron,
        Pon,
        Chi,
        Pass,
        Flow,

        /// <summary>报听（叫胡）。</summary>
        DeclareCall,
    }

    public sealed record Button(Kind Kind, string Label, RectF Rect, int Option = 0, bool Enabled = true);

    /// <summary>一个按钮在某一状态下的三色：底色 / 边框 / 文字。</summary>
    public readonly record struct ButtonColors(Color Fill, Color Border, Color Text);

    /// <summary>
    /// 语义色基准表（唯一改动点）。按钮底色取基准色的深色版，边框 / 文字取亮色版；
    /// 灰显态不区分 Kind，统一去饱和 + 灰字。
    /// </summary>
    private static readonly Dictionary<Kind, Color> Accents = new()
    {
        [Kind.Chi] = Color.Rgb(0x3B, 0x82, 0xF6),          // 蓝  #3B82F6
        [Kind.Pon] = Color.Rgb(0xE5, 0x39, 0x9B),          // 品红 #E5399B
        [Kind.Kan] = Color.Rgb(0x3F, 0xB9, 0x50),          // 绿  #3FB950
        [Kind.DeclareCall] = Color.Rgb(0x22, 0xC3, 0xD6),  // 青  #22C3D6
        [Kind.Tsumo] = Color.Rgb(0xF2, 0xC9, 0x4C),        // 黄  #F2C94C（和牌）
        [Kind.Ron] = Color.Rgb(0xF2, 0xC9, 0x4C),          // 荣和同用黄
        [Kind.Pass] = Color.Rgb(0x7E, 0x8C, 0x83),         // 中性灰绿
        [Kind.Flow] = Color.Rgb(0x7E, 0x8C, 0x83),
    };

    private static readonly ButtonColors DisabledColors =
        new(Color.Rgb(34, 40, 36), Color.Rgb(89, 99, 92), Color.Rgb(140, 148, 142));

    private readonly List<Button> _buttons = new();
    private float _pointerX;
    private float _pointerY;
    private bool _hasPointer;

    public IReadOnlyList<Button> Buttons => _buttons;

    public bool HasAny => _buttons.Count > 0;

    /// <summary>语义色基准（供 SVG 导出 / 调色板复用）。</summary>
    public static Color Accent(Kind kind) => Accents.TryGetValue(kind, out var c) ? c : Color.Rgb(140, 170, 150);

    /// <summary>按钮在 可用 / 悬停 / 灰显 三态下的配色。</summary>
    public static ButtonColors ColorsFor(Kind kind, bool hover = false, bool enabled = true)
    {
        if (!enabled)
        {
            return DisabledColors;
        }

        var accent = Accent(kind);
        if (hover)
        {
            return new ButtonColors(
                Darken(accent, 0.40f),
                Mix(accent, Color.White, 0.28f),
                Mix(accent, Color.White, 0.82f));
        }

        return new ButtonColors(
            Darken(accent, 0.26f),
            accent,
            Mix(accent, Color.White, 0.68f));
    }

    /// <summary>SVG / 自动化用的稳定 id 片段：吃/碰/杠/报听/和。</summary>
    public static string SemanticId(Kind kind) => kind switch
    {
        Kind.Chi => "chi",
        Kind.Pon => "pon",
        Kind.Kan => "kan",
        Kind.DeclareCall => "call",
        Kind.Tsumo or Kind.Ron => "win",
        Kind.Pass => "pass",
        Kind.Flow => "flow",
        _ => "action",
    };

    private static Color Darken(Color c, float factor) =>
        new((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor), c.A);

    private static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)(a.A + (b.A - a.A) * t));
    }

    /// <summary>记录指针位置（用于悬停态）；宿主在指针离开按钮条时调用 <see cref="ClearPointer"/>。</summary>
    public void SetPointer(float x, float y)
    {
        _pointerX = x;
        _pointerY = y;
        _hasPointer = true;
    }

    public void ClearPointer() => _hasPointer = false;

    /// <summary>按快照重建按钮（每帧调用，纯计算）。</summary>
    public void Build(TableViewModel vm, TableLayoutSpec layout)
    {
        _buttons.Clear();
        if (vm.Phase is "round_end" or "match_over" or "idle")
        {
            return;
        }

        var hand = layout.HandRects.Length > 0 ? layout.HandRects[0] : new RectF(0, 0, 0, 0);
        if (hand.W <= 0)
        {
            return;
        }

        var items = new List<(Kind Kind, string Label, int Option, bool Enabled)>();

        if ((vm.Actions & HumanAction.Kan) != 0)
        {
            items.Add((Kind.Kan, "杠", 0, true));
        }

        if ((vm.Actions & HumanAction.Tsumo) != 0)
        {
            items.Add((Kind.Tsumo, "和牌", 0, true));
        }
        else if ((vm.Actions & HumanAction.Ron) != 0)
        {
            items.Add((Kind.Ron, "荣和", 0, true));
        }

        if ((vm.Actions & HumanAction.Pon) != 0)
        {
            items.Add((Kind.Pon, "碰", 0, true));
        }

        if ((vm.Actions & HumanAction.Chi) != 0)
        {
            if (vm.ActionLabels.Count <= 1)
            {
                items.Add((Kind.Chi, "吃", 0, true));
            }
            else
            {
                for (int i = 0; i < vm.ActionLabels.Count; i++)
                {
                    items.Add((Kind.Chi, vm.ActionLabels[i], i, true));
                }
            }
        }

        if ((vm.Actions & HumanAction.Pass) != 0)
        {
            items.Add((Kind.Pass, "过", 0, true));
        }

        if ((vm.Actions & HumanAction.Flow) != 0)
        {
            items.Add((Kind.Flow, "流局", 0, true));
        }

        // 报听：只要自己手里有牌就占位（不可用时灰显），已报听则显示状态。
        if (vm.Seats.Count > 0 && vm.Seats[0].Hand.Count > 0)
        {
            bool callable = (vm.Actions & HumanAction.DeclareCall) != 0;
            items.Add((Kind.DeclareCall, vm.IsCalled ? "已报听" : "报听", 0, callable));
        }

        if (items.Count == 0)
        {
            return;
        }

        const float bw = 96f, gap = 10f;
        var bar = layout.ActionBarRect;
        float bh = bar.H > 0f ? bar.H : 46f;
        float totalW = items.Count * bw + (items.Count - 1) * gap;
        float x = bar.W > 0f ? bar.CenterX - totalW / 2f : hand.CenterX - totalW / 2f;
        float y = bar.W > 0f ? bar.Y : hand.Y - bh - 16f;
        if (y < 8)
        {
            y = 8;
        }

        foreach (var (kind, label, option, enabled) in items)
        {
            _buttons.Add(new Button(kind, label, new RectF(x, y, bw, bh), option, enabled));
            x += bw + gap;
        }
    }

    public void Draw(ICanvas c, double time = 0)
    {
        foreach (var b in _buttons)
        {
            bool hover = b.Enabled && _hasPointer && b.Rect.Contains(_pointerX, _pointerY);
            DrawButton(c, b.Kind, b.Label, b.Rect, b.Enabled, hover, time);
        }
    }

    /// <summary>
    /// 画一颗按钮（三态）；游戏内与 <c>--render-actions</c> 调色板共用同一条绘制路径。
    /// 悬停（=选中）时额外画两枚绕边缘旋转的白点 + 边缘呼吸闪烁（0–50–0，500ms）。
    /// </summary>
    public static void DrawButton(ICanvas c, Kind kind, string label, RectF rect, bool enabled, bool hover = false,
        double time = 0)
    {
        var colors = ColorsFor(kind, hover, enabled);
        c.FillRoundRect(rect, 10, colors.Fill.WithAlpha((byte)(enabled ? (hover ? 245 : 230) : 200)));
        c.StrokeRoundRect(rect, 10, colors.Border, hover ? 2.2f : 1.5f);
        c.DrawText(label, rect, colors.Text, new TextStyle(label.Length > 4 ? 15 : 18, Bold: true));

        if (!enabled || !hover)
        {
            return;
        }

        // 0–50–0 线性呼吸（500ms 一循环）
        double frac = time % 0.5 / 0.5;
        float pulse = (float)(0.5 * (1.0 - Math.Abs(2.0 * frac - 1.0)));
        var white = Color.Rgb(255, 255, 255).WithAlpha((byte)(pulse * 255));
        c.StrokeRoundRect(rect, 10, white, 1.8f);

        // 两枚光点沿按钮边缘反向旋转（约 1.2s 一圈）
        double angle = time / 1.2 * Math.PI * 2.0;
        float rx = rect.W / 2f;
        float ry = rect.H / 2f;
        for (int i = 0; i < 2; i++)
        {
            double a = angle + i * Math.PI;
            float dx = rect.CenterX + (float)(Math.Cos(a) * rx);
            float dy = rect.CenterY + (float)(Math.Sin(a) * ry);
            c.FillCircle(dx, dy, 3.6f, white);
            c.FillCircle(dx, dy, 8f, white.WithAlpha((byte)(pulse * 70)));
        }
    }

    /// <summary>画待听牌提示（等待的牌 + 张数），在动作条上方；真听牌时常显。</summary>
    public void DrawTenpai(ICanvas c, TableViewModel vm, TableLayoutSpec layout)
    {
        if (vm.TenpaiTiles.Count == 0)
        {
            return;
        }

        var hand = layout.HandRects.Length > 0 ? layout.HandRects[0] : new RectF(0, 0, 0, 0);
        if (hand.W <= 0)
        {
            return;
        }

        const float tw = 40f, th = 56f, gap = 6f;
        float labelW = 70f;
        float totalW = labelW + vm.TenpaiTiles.Count * (tw + gap);
        float x = hand.CenterX - totalW / 2f;
        float y = hand.Y - 46f - 16f - th - 8f;

        var box = new RectF(x - 8, y - 6, totalW + 8, th + 12);
        c.FillRoundRect(box, 8, Color.Rgb(0, 0, 0).WithAlpha(150));
        c.DrawText("听牌", new RectF(x, y + th / 2f - 10, labelW, 20),
            Color.Rgb(255, 226, 140), new TextStyle(16, Bold: true, Center: false));

        float tx = x + labelW;
        foreach (var id in vm.TenpaiTiles)
        {
            c.DrawTile(id, new RectF(tx, y, tw, th));
            tx += tw + gap;
        }
    }

    public Button? HitTest(float x, float y)
    {
        foreach (var b in _buttons)
        {
            if (b.Rect.Contains(x, y))
            {
                return b;
            }
        }

        return null;
    }
}
