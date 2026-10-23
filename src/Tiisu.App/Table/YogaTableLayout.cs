using System;
using Tiisu.App.Rendering;
using YogaSharp.Core;
using YogaSharp.Layout;

namespace Tiisu.App.Table;

/// <summary>
/// 用 Facebook Yoga 计算牌桌布局（替换自写的 <see cref="Flex"/> 引擎）。
///
/// 树结构与 V4 完全相同：
///   root(pad 16, column, align center)
///     ├─ 纵向列 739 × (H-32)，上下 pad 24
///     │    ├─ 手牌框 739×68
///     │    ├─ 弹性间隙 flexGrow 1
///     │    ├─ 牌河区 380×260
///     │    ├─ 间隔 14
///     │    ├─ 中央板 400×400
///     │    ├─ 间隔 14
///     │    ├─ 牌河区 380×260
///     │    ├─ 弹性间隙 flexGrow 1
///     │    └─ 手牌框 739×68
///     ├─ 左家手牌框（绝对定位，左中）
///     └─ 右家手牌框（绝对定位，右中）
///
/// 结果写回 <see cref="TableLayoutSpec"/>，绘制仍由 <see cref="TableSceneSkia"/> 完成。
/// </summary>
public static class YogaTableLayout
{
    public static void Apply(TableLayoutSpec l)
    {
        float cw = l.CanvasW;
        float ch = l.CanvasH;
        float inner = ch - l.Margin * 2f;

        using var root = new YogaNode
        {
            Width = cw,
            Height = ch,
            FlexDirection = FlexDirection.Column,
            AlignItems = AlignItems.Center,
        };
        SetPad(root, l.Margin);

        var column = new YogaNode
        {
            Width = l.FrameLen,
            Height = inner,
            FlexDirection = FlexDirection.Column,
            AlignItems = AlignItems.Center,
            PaddingTop = l.EdgePad,
            PaddingBottom = l.EdgePad,
        };

        var topHand = new YogaNode { Width = l.FrameLen, Height = l.FrameDepth };
        var growTop = new YogaNode { Width = 0, Height = 0, FlexGrow = 1 };
        var riverTop = new YogaNode { Width = l.RiverSpan, Height = l.RiverDepth };
        var sp1 = new YogaNode { Width = 0, Height = l.PlateGap };
        var plate = new YogaNode { Width = l.CenterSize, Height = l.CenterSize };
        var sp2 = new YogaNode { Width = 0, Height = l.PlateGap };
        var riverBottom = new YogaNode { Width = l.RiverSpan, Height = l.RiverDepth };
        var growBottom = new YogaNode { Width = 0, Height = 0, FlexGrow = 1 };
        var selfHand = new YogaNode { Width = l.FrameLen, Height = l.FrameDepth };

        column.AddChild(topHand);
        column.AddChild(growTop);
        column.AddChild(riverTop);
        column.AddChild(sp1);
        column.AddChild(plate);
        column.AddChild(sp2);
        column.AddChild(riverBottom);
        column.AddChild(growBottom);
        column.AddChild(selfHand);

        // 绝对定位：Yoga 的绝对坐标相对父级 border box（这里就是画布本身）
        float sideTop = l.Margin + (inner - l.FrameLen) / 2f;
        var leftHand = new YogaNode
        {
            Width = l.FrameDepth,
            Height = l.FrameLen,
            PositionType = PositionType.Absolute,
            PositionLeft = l.Margin + l.EdgePad,
            PositionTop = sideTop,
        };
        var rightHand = new YogaNode
        {
            Width = l.FrameDepth,
            Height = l.FrameLen,
            PositionType = PositionType.Absolute,
            PositionRight = l.Margin + l.EdgePad,
            PositionTop = sideTop,
        };

        root.AddChild(column);
        root.AddChild(leftHand);
        root.AddChild(rightHand);

        root.CalculateLayout(cw, ch);

        var colOrigin = new RectF(column.Layout.X, column.Layout.Y, 0, 0);

        l.PlateRect = Offset(colOrigin, plate);

        l.HandRects[0] = Offset(colOrigin, selfHand);
        l.HandRects[2] = Offset(colOrigin, topHand);
        l.HandRects[3] = ToRect(leftHand);
        l.HandRects[1] = ToRect(rightHand);

        ApplyDefaultRegions(l);
    }

    /// <summary>
    /// 把可编辑区域重置成由 Yoga 结果推导的默认值。
    /// 每次 <see cref="Apply"/> 都重算，编辑器「恢复默认」只要再调一次即可。
    /// </summary>
    private static void ApplyDefaultRegions(TableLayoutSpec l)
    {
        l.OverridesActive = false;
        l.HudRect = new RectF(24, 20, 300, 34);
        l.ActionBarRect = new RectF(
            l.HandRects[0].CenterX - 300f,
            Math.Max(8f, l.HandRects[0].Y - 46f - 16f),
            600f,
            46f);
        l.SettlementRect = TableLayoutSpec.DefaultSettlementRect;
        l.SettlementHandRect = TableLayoutSpec.DefaultSettlementHandRect;

        // 牌河：先清空覆盖，让 River() 走中央板推导，再固化成可编辑矩形。
        for (int seat = 0; seat < 4; seat++)
        {
            l.RiverRects[seat] = default;
        }

        for (int seat = 0; seat < 4; seat++)
        {
            l.RiverRects[seat] = l.River(seat).Bounds;
        }

        // 副露区参考框：手牌框右手侧（与绘制同一套轴向规则）。
        const float meldBand = 210f;
        for (int seat = 0; seat < 4; seat++)
        {
            var f = l.HandRects[seat];
            var (horizontal, _, rightDir) = TableLayoutSpec.Axis(seat);
            l.MeldRects[seat] = horizontal
                ? new RectF(
                    rightDir > 0 ? f.Right + l.HandMeldGap : f.X - l.HandMeldGap - meldBand,
                    f.Y, meldBand, f.H)
                : new RectF(
                    f.X,
                    rightDir > 0 ? f.Bottom + l.HandMeldGap : f.Y - l.HandMeldGap - meldBand,
                    f.W, meldBand);
        }
    }

    private static void SetPad(YogaNode n, float p)
    {
        n.PaddingLeft = p;
        n.PaddingRight = p;
        n.PaddingTop = p;
        n.PaddingBottom = p;
    }

    private static RectF ToRect(YogaNode n)
    {
        var a = n.Layout;
        return new RectF(a.X, a.Y, a.Width, a.Height);
    }

    private static RectF Offset(RectF origin, YogaNode n)
    {
        var a = n.Layout;
        return new RectF(origin.X + a.X, origin.Y + a.Y, a.Width, a.Height);
    }
}
