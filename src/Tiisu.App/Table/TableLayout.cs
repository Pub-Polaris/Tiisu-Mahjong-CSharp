using System;
using Tiisu.App.Rendering;

namespace Tiisu.App.Table;

/// <summary>
/// 雀魂式牌桌布局（1280×720 逻辑画布）：
///   · 中央牌桌板（风向/点数/局数/剩余）
///   · 四家牌河围成回字形（每行 6 张，行向远离中央板）
///   · 自家 13 张 + 摸牌单独空一格；副露贴在手牌右端
///   · 对家牌河/副露旋转 180°，上下家旋转 ±90°
/// 立直相关（立直棒/立直宣言）本规则不存在，不画。
/// </summary>
public static class TableLayout
{
    public const float Width = 1280f;
    public const float Height = 720f;

    public static readonly RectF Table = new(10, 10, Width - 20, Height - 20);

    // ── 中央牌桌板 ──
    public static readonly RectF CenterPlate = new(536, 246, 208, 208);

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
    public const float RiverDepth = 150f;   // 每侧牌河最大深度（超过就压缩行高）

    // ── 自家手牌 / 副露 ──
    public static readonly RectF HandArea = new(240, 620, 790, 82);
    public static readonly RectF OwnMeldArea = new(1044, 620, 212, 82);

    // ── 对家手牌（牌背）──
    public static readonly RectF TopHandArea = new(380, 22, 520, 46);
    public const float BackTileW = 36f;
    public const float BackTileH = 46f;
    public const float BackGap = 2f;

    // ── 上下家手牌（竖排牌背）──
    public static readonly RectF LeftHandArea = new(18, 196, 50, 430);
    public static readonly RectF RightHandArea = new(1212, 196, 50, 430);
    public const float SideBackW = 44f;   // 侧边牌背：宽（横轴）
    public const float SideBackH = 30f;   // 侧边牌背：高（竖轴）
    public const float SideBackStep = 32f;

    // ── 对家副露（贴对家牌河外侧）──
    public static readonly RectF TopMeldArea = new(90, 74, 300, 44);
    public static readonly RectF LeftMeldArea = new(332, 250, 46, 210);
    public static readonly RectF RightMeldArea = new(902, 250, 46, 210);

    // ── 四角玩家信息 ──
    public static readonly RectF InfoTL = new(20, 20, 250, 46);
    public static readonly RectF InfoTR = new(1010, 20, 250, 46);
    public static readonly RectF InfoBL = new(84, 534, 170, 46);
    public static readonly RectF InfoBR = new(1026, 534, 170, 46);

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
