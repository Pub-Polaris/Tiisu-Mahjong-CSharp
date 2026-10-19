using System;
using Tiisu.App.Rendering;

namespace Tiisu.App.Table;

/// <summary>
/// 牌桌布局（北极星 4:3 改版，坐标取自他在 Illustrator 里放大的边界框）：
///   canvas 1024×768；画布外由宿主纯色 letterbox。
///   每家的手牌带 + **右手侧的副露区**（与手牌带重叠，副露后手牌缩回左/上/下段）。
///   四家牌河紧贴中央板围成回字形。
/// </summary>
public static class TableLayout
{
    // 4:3
    public const float Width = 1024f;
    public const float Height = 768f;

    public static readonly RectF Table = new(10, 10, Width - 20, Height - 20);

    // ── 中央牌桌板 ──
    public static readonly RectF CenterPlate = new(405.2f, 277.2f, 213.6f, 213.6f);

    // ── 牌尺寸 ──
    public const float HandTileW = 56f;
    public const float HandTileH = 78f;
    public const float HandGap = 2f;
    public const float DrawnGap = 12f;

    public const float RiverTileW = 30f;
    public const float RiverTileH = 42f;
    public const float RiverGap = 2f;
    public const int RiverCols = 6;

    public static float RiverRowW => RiverCols * (RiverTileW + RiverGap) - RiverGap;

    // ── 四家的手牌带（不含副露区）──
    // 自家（下）：手牌左段，副露在右段
    public static readonly RectF HandArea = new(170.2f, 639.3f, 462f, 96.1f);
    public static readonly RectF OwnMeldArea = new(637f, 639.3f, 216.6f, 96.1f);

    // 对家（上）：手牌左段，副露在右段
    public static readonly RectF TopHandArea = new(170.2f, 32.6f, 462f, 96.1f);
    public static readonly RectF TopMeldArea = new(637f, 32.6f, 216.6f, 96.1f);

    // 上家（左）：手牌上半段，副露在下段
    public static readonly RectF LeftHandArea = new(23.8f, 42.1f, 96.1f, 468f);
    public static readonly RectF LeftMeldArea = new(23.8f, 514.1f, 96.1f, 211.5f);

    // 下家（右）：手牌下半段，副露在上段
    public static readonly RectF RightHandArea = new(904f, 253.9f, 96.1f, 472f);
    public static readonly RectF RightMeldArea = new(904f, 42.4f, 96.1f, 211.5f);

    // ── 牌背尺寸 ──
    public const float BackTileW = 32f;
    public const float BackTileH = 46f;
    public const float BackGap = 2f;
    public const float SideBackW = 42f;
    public const float SideBackH = 30f;
    public const float SideBackStep = 32f;

    // ── 四家牌河（取自改版 SVG）──
    public const float RiverInset = 8f;

    /// <summary>某一家牌河的几何：起点块、行/列步进、朝向角。</summary>
    public static RiverGeometry River(int seat)
    {
        float w = RiverTileW, h = RiverTileH, gap = RiverGap;

        switch (seat)
        {
            case 0: // 自家（下）：行向下，从中央板下沿往外
                return new RiverGeometry(
                    new RectF(406.2f, 529.3f, 211.5f, 90.6f),
                    dxCol: w + gap, dyCol: 0,
                    dxRow: 0, dyRow: h + gap,
                    angle: 0f);
            case 1: // 下家（右）：行向右，牌转 90°
                return new RiverGeometry(
                    new RectF(657.3f, 278.2f, 90.6f, 211.5f),
                    dxCol: 0, dyCol: w + gap,
                    dxRow: h + gap, dyRow: 0,
                    angle: 90f);
            case 2: // 对家（上）：行向上，牌转 180°
                return new RiverGeometry(
                    new RectF(406.2f, 148.1f, 211.5f, 90.6f),
                    dxCol: w + gap, dyCol: 0,
                    dxRow: 0, dyRow: -(h + gap),
                    angle: 180f);
            default: // 上家（左）：行向左，牌转 -90°
                return new RiverGeometry(
                    new RectF(276.1f, 278.3f, 90.6f, 211.5f),
                    dxCol: 0, dyCol: w + gap,
                    dxRow: -(h + gap), dyRow: 0,
                    angle: -90f);
        }
    }

    public readonly record struct RiverGeometry(
        RectF Bounds, float dxCol, float dyCol, float dxRow, float dyRow, float angle);
}
