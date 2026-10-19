using System;
using Tiisu.App.Rendering;

namespace Tiisu.App.Table;

/// <summary>
/// 雀魂式牌桌布局，**画布锁 4:3（1024×768）**；画布外由宿主用纯色 letterbox 填充。
///   · 中央牌桌板（风向/点数/局数/剩余）
///   · 四家牌河围成回字形（每行 6 张，行向远离中央板）
///   · 自家 13 张 + 摸牌单独空一格；副露贴在手牌右端
///   · 对家牌河/副露旋转 180°，上下家旋转 ±90°
/// 立直相关（立直棒/立直宣言）本规则不存在，不画。
/// </summary>
public static class TableLayout
{
    // 4:3
    public const float Width = 1024f;
    public const float Height = 768f;

    public static readonly RectF Table = new(10, 10, Width - 20, Height - 20);

    // ── 中央牌桌板 ──
    public static readonly RectF CenterPlate = new(412, 264, 200, 200);

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

    public const float RiverInset = 8f;
    public const float RiverDepth = 186f;

    // ── 自家手牌 / 副露 ──
    public static readonly RectF HandArea = new(140, 664, 640, 90);
    public static readonly RectF OwnMeldArea = new(800, 664, 216, 90);

    // ── 对家手牌（牌背）──
    public static readonly RectF TopHandArea = new(292, 16, 440, 46);
    public const float BackTileW = 32f;
    public const float BackTileH = 46f;
    public const float BackGap = 2f;

    // ── 上下家手牌（竖排牌背）──
    public static readonly RectF LeftHandArea = new(10, 140, 46, 480);
    public static readonly RectF RightHandArea = new(968, 140, 46, 480);
    public const float SideBackW = 42f;
    public const float SideBackH = 28f;
    public const float SideBackStep = 30f;

    // ── 别家副露（贴各自手牌，由场景居中）──
    public static readonly RectF TopMeldArea = new(300, 66, 424, 44);
    public static readonly RectF LeftMeldArea = new(58, 250, 44, 280);
    public static readonly RectF RightMeldArea = new(922, 250, 44, 280);

    /// <summary>某一家牌河的几何：起点块、行/列步进、朝向角（牌要转多少度才朝向该家）。</summary>
    public static RiverGeometry River(int seat)
    {
        var plate = CenterPlate;
        float w = RiverTileW, h = RiverTileH, gap = RiverGap;
        float halfRow = RiverRowW / 2f;

        // 起点 = 紧贴中央板的那一格；行沿"远离中央板"的方向增长。
        switch (seat)
        {
            case 0: // 自家（下）：行向下
                return new RiverGeometry(
                    new RectF(plate.CenterX - halfRow, plate.Bottom + RiverInset, RiverRowW, RiverDepth),
                    dxCol: w + gap, dyCol: 0,
                    dxRow: 0, dyRow: h + gap,
                    angle: 0f);
            case 1: // 下家（右）：行向右，牌转 90°
                return new RiverGeometry(
                    new RectF(plate.Right + RiverInset, plate.CenterY - halfRow, RiverDepth, RiverRowW),
                    dxCol: 0, dyCol: w + gap,
                    dxRow: h + gap, dyRow: 0,
                    angle: 90f);
            case 2: // 对家（上）：行向上，牌转 180°
                return new RiverGeometry(
                    new RectF(plate.CenterX - halfRow, plate.Top - RiverInset - h, RiverRowW, RiverDepth),
                    dxCol: w + gap, dyCol: 0,
                    dxRow: 0, dyRow: -(h + gap),
                    angle: 180f);
            default: // 上家（左）：行向左，牌转 -90°
                return new RiverGeometry(
                    new RectF(plate.Left - RiverInset - h, plate.CenterY - halfRow, RiverDepth, RiverRowW),
                    dxCol: 0, dyCol: w + gap,
                    dxRow: -(h + gap), dyRow: 0,
                    angle: -90f);
        }
    }

    public readonly record struct RiverGeometry(
        RectF Bounds, float dxCol, float dyCol, float dxRow, float dyRow, float angle);
}
