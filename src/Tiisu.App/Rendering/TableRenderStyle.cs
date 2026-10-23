using System.Text.Json.Serialization;

namespace Tiisu.App.Rendering;

/// <summary>
/// 牌桌观感。Flat 与旧版逐像素一致；Mixed 是默认档（Light 的牌身 + 真透视桌面）；
/// Light25D / Medium25D 保留原来的梯形桌面档位。
/// 布局与命中测试仍用原来的逻辑矩形（<see cref="Table.TableLayoutSpec"/>），不参与输入坐标。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TableRenderStyle
{
    Flat,

    /// <summary>方案 C：牌身用 Light 档厚度/投影 + 左上高光边，桌面试真透视矩阵。</summary>
    Mixed,

    Light25D,
    Medium25D,
}

/// <summary>
/// 一种观感对应的绘制参数。所有数值都是逻辑画布像素（1200×1200）。
/// 牌身厚度/投影会按牌的实际尺寸缩放（副露的小牌不会顶着一圈 8px 边）。
/// </summary>
public sealed class TableRenderMetrics
{
    /// <summary>牌身右下侧厚度（0 = 纯平面）。</summary>
    public float TileThickness { get; init; }

    public float ShadowOffsetX { get; init; }
    public float ShadowOffsetY { get; init; }
    public float ShadowBlur { get; init; }
    public byte ShadowAlpha { get; init; }

    /// <summary>桌面顶边相对底边向内收进的距离（上远下近的梯形）。0 = 矩形。</summary>
    public float TableTopInset { get; init; }

    /// <summary>桌面下沿的厚度亮/暗边（0 = 不画）。</summary>
    public float TableBottomLip { get; init; }

    public byte VignetteAlpha { get; init; }
    public byte EdgeHighlightAlpha { get; init; }

    public Color TableTop { get; init; }
    public Color TableBottom { get; init; }
    public Color TableLip { get; init; }
    public Color SurroundTop { get; init; }
    public Color SurroundBottom { get; init; }

    public Color TileSideTop { get; init; }
    public Color TileSideBottom { get; init; }
    public Color TileBackSideTop { get; init; }
    public Color TileBackSideBottom { get; init; }

    /// <summary>正面左上角的一圈微高光，让牌面从侧面上「浮」起来。</summary>
    public byte FaceBevelAlpha { get; init; }

    /// <summary>牌身左上高光边（实体牌受光面）。0 = 不画；Mixed 档打开。</summary>
    public byte TileHighlightAlpha { get; init; }

    /// <summary>桌面层是否走真透视矩阵（<see cref="ICanvas.ConcatPerspective"/>），而不是梯形模拟。</summary>
    public bool UsePerspective { get; init; }

    // 真透视参数：桌面平面绕 X 轴后倾 rotateX，距相机 distance，再按 fov 投影。
    // aspect 用画布真实宽高比（1200×1200 为 1.0）；near/far 只影响深度映射，不影响平面上的 x/y。
    public float PerspectiveFov { get; init; } = 55f;
    public float PerspectiveAspect { get; init; } = 1f;
    public float PerspectiveNear { get; init; } = 1f;
    public float PerspectiveFar { get; init; } = 100f;
    public float PerspectiveRotateX { get; init; } = 55f;

    /// <summary>相机到桌面中心的距离（逻辑像素）。越小透视越强。</summary>
    public float PerspectiveDistance { get; init; } = 1010f;

    /// <summary>投影后等比缩放，用来把桌面铺满画布。</summary>
    public float PerspectiveZoom { get; init; } = 1.16f;

    /// <summary>投影后的整体竖直位移（负 = 上移）。</summary>
    public float PerspectiveOffsetY { get; init; } = -282f;

    public static readonly TableRenderMetrics Flat = new()
    {
        TileThickness = 0f,
        TableTopInset = 0f,
        TableBottomLip = 0f,
        VignetteAlpha = 0,
        EdgeHighlightAlpha = 0,
        FaceBevelAlpha = 0,
    };

    public static readonly TableRenderMetrics Light = new()
    {
        TileThickness = 4f,
        ShadowOffsetX = 1.5f,
        ShadowOffsetY = 2.5f,
        ShadowBlur = 2f,
        ShadowAlpha = 120,
        TableTopInset = 10f,
        TableBottomLip = 4f,
        VignetteAlpha = 46,
        EdgeHighlightAlpha = 80,
        TableTop = Color.Rgb(38, 60, 47),
        TableBottom = Color.Rgb(54, 84, 64),
        TableLip = Color.Rgb(26, 40, 31),
        SurroundTop = Color.Rgb(14, 22, 18),
        SurroundBottom = Color.Rgb(10, 16, 13),
        TileSideTop = Color.Rgb(188, 174, 136),
        TileSideBottom = Color.Rgb(132, 116, 82),
        TileBackSideTop = Color.Rgb(28, 88, 134),
        TileBackSideBottom = Color.Rgb(16, 52, 80),
        FaceBevelAlpha = 70,
    };

    /// <summary>
    /// 方案 C（默认）：牌身沿用 Light 档的厚度 4 / 投影 2，并加左上高光边；
    /// 桌面不用梯形，改走真透视矩阵（55° FOV、后倾 55°、相机距离 1010）。
    /// </summary>
    public static readonly TableRenderMetrics Mixed = new()
    {
        TileThickness = 4f,
        ShadowOffsetX = 1.5f,
        ShadowOffsetY = 2f,
        ShadowBlur = 2f,
        ShadowAlpha = 120,
        TableTopInset = 0f,
        TableBottomLip = 6f,
        VignetteAlpha = 46,
        EdgeHighlightAlpha = 90,
        TableTop = Color.Rgb(38, 60, 47),
        TableBottom = Color.Rgb(54, 84, 64),
        TableLip = Color.Rgb(26, 40, 31),
        SurroundTop = Color.Rgb(14, 22, 18),
        SurroundBottom = Color.Rgb(10, 16, 13),
        TileSideTop = Color.Rgb(188, 174, 136),
        TileSideBottom = Color.Rgb(132, 116, 82),
        TileBackSideTop = Color.Rgb(28, 88, 134),
        TileBackSideBottom = Color.Rgb(16, 52, 80),
        FaceBevelAlpha = 70,
        TileHighlightAlpha = 200,
        UsePerspective = true,
    };

    public static readonly TableRenderMetrics Medium = new()
    {
        TileThickness = 8f,
        ShadowOffsetX = 3f,
        ShadowOffsetY = 5f,
        ShadowBlur = 4f,
        ShadowAlpha = 150,
        TableTopInset = 58f,
        TableBottomLip = 8f,
        VignetteAlpha = 70,
        EdgeHighlightAlpha = 120,
        TableTop = Color.Rgb(28, 47, 37),
        TableBottom = Color.Rgb(64, 101, 74),
        TableLip = Color.Rgb(20, 32, 25),
        SurroundTop = Color.Rgb(12, 19, 15),
        SurroundBottom = Color.Rgb(9, 14, 11),
        TileSideTop = Color.Rgb(176, 160, 120),
        TileSideBottom = Color.Rgb(104, 88, 58),
        TileBackSideTop = Color.Rgb(24, 78, 120),
        TileBackSideBottom = Color.Rgb(11, 42, 66),
        FaceBevelAlpha = 90,
    };

    public static TableRenderMetrics For(TableRenderStyle style) => style switch
    {
        TableRenderStyle.Mixed => Mixed,
        TableRenderStyle.Light25D => Light,
        TableRenderStyle.Medium25D => Medium,
        _ => Flat,
    };

    public bool HasTileDepth => TileThickness > 0f;
}
