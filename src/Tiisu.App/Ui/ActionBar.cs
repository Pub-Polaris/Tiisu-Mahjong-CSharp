using System;
using System.Collections.Generic;
using Tiisu.App.Rendering;
using Tiisu.App.Table;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 手牌上方的动作按钮条：杠 / 和 / 碰 / 吃（多组各一颗）/ 过 / 流局 + 听牌提示开关。
/// 点击返回对应命令；键盘快捷键仍可用（H/P/C/K/Space/F）。
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
        Tenpai,
    }

    public sealed record Button(Kind Kind, string Label, RectF Rect, int Option = 0);

    private readonly List<Button> _buttons = new();
    private bool _showTenpai;

    public IReadOnlyList<Button> Buttons => _buttons;

    public bool ShowTenpai => _showTenpai;

    public void ToggleTenpai() => _showTenpai = !_showTenpai;

    public bool HasAny => _buttons.Count > 0;

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

        var items = new List<(Kind Kind, string Label, int Option)>();

        if ((vm.Actions & HumanAction.Kan) != 0)
        {
            items.Add((Kind.Kan, "杠", 0));
        }

        if ((vm.Actions & HumanAction.Tsumo) != 0)
        {
            items.Add((Kind.Tsumo, "和牌", 0));
        }
        else if ((vm.Actions & HumanAction.Ron) != 0)
        {
            items.Add((Kind.Ron, "荣和", 0));
        }

        if ((vm.Actions & HumanAction.Pon) != 0)
        {
            items.Add((Kind.Pon, "碰", 0));
        }

        if ((vm.Actions & HumanAction.Chi) != 0)
        {
            if (vm.ActionLabels.Count <= 1)
            {
                items.Add((Kind.Chi, "吃", 0));
            }
            else
            {
                for (int i = 0; i < vm.ActionLabels.Count; i++)
                {
                    items.Add((Kind.Chi, vm.ActionLabels[i], i));
                }
            }
        }

        if ((vm.Actions & HumanAction.Pass) != 0)
        {
            items.Add((Kind.Pass, "过", 0));
        }

        if ((vm.Actions & HumanAction.Flow) != 0)
        {
            items.Add((Kind.Flow, "流局", 0));
        }

        // 听牌提示：只要手里有牌就给（自摸/副露窗口之外也允许看）
        if (vm.Seats.Count > 0 && vm.Seats[0].Hand.Count > 0)
        {
            items.Add((Kind.Tenpai, _showTenpai ? "听牌 ✓" : "听牌", 0));
        }

        if (items.Count == 0)
        {
            return;
        }

        const float bw = 96f, bh = 46f, gap = 10f;
        float totalW = items.Count * bw + (items.Count - 1) * gap;
        float x = hand.CenterX - totalW / 2f;
        float y = hand.Y - bh - 16f;
        if (y < 8)
        {
            y = 8;
        }

        foreach (var (kind, label, option) in items)
        {
            _buttons.Add(new Button(kind, label, new RectF(x, y, bw, bh), option));
            x += bw + gap;
        }
    }

    public void Draw(ICanvas c)
    {
        foreach (var b in _buttons)
        {
            bool accent = b.Kind is Kind.Tsumo or Kind.Ron;
            var fill = accent ? Color.Rgb(120, 40, 36) : Color.Rgb(18, 30, 24);
            c.FillRoundRect(b.Rect, 10, fill.WithAlpha(230));
            c.StrokeRoundRect(b.Rect, 10, accent ? Color.Rgb(255, 196, 120) : Color.Rgb(140, 170, 150), 1.5f);
            c.DrawText(b.Label, b.Rect,
                accent ? Color.Rgb(255, 240, 210) : Color.Rgb(230, 240, 230),
                new TextStyle(b.Label.Length > 4 ? 15 : 18, Bold: true));
        }
    }

    /// <summary>画听牌提示（等待的牌 + 张数），在动作条上方。</summary>
    public void DrawTenpai(ICanvas c, TableViewModel vm, TableLayoutSpec layout)
    {
        if (!_showTenpai || vm.TenpaiTiles.Count == 0)
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
