using System.Text.Json.Serialization;

namespace Tiisu.App.Rendering;

/// <summary>
/// 牌桌观感。Flat 与旧版逐像素一致；Mixed 是默认档（更厚的牌身 + 真透视桌面 + 透视中央板）；
/// Light25D / Medium25D 保留原来的梯形桌面档位。
/// 布局与命中测试仍用原来的逻辑矩形（<see cref="Table.TableLayoutSpec"/>），不参与输入坐标。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TableRenderStyle
{
    Flat,

    /// <summary>已废弃：仅为了兼容旧 settings.json 里的值，绘制一律按 <see cref="Flat"/> 处理。</summary>
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

    /// <summary>
    /// 透视桌面在逻辑坐标里的矩形（可越出画布：投影后会收回画布内）。
    /// <c>null</c> = 按布局 <see cref="Table.TableLayoutSpec.Margin"/> 内缩画布。
    /// Mixed 用 (60, −62, 1080, 1262)：投影后是左右 ~45 / 上 ~55 / 下 ~55 的梯形，
    /// 外圈留给深色渐变背景。
    /// </summary>
    public RectF? TableQuad { get; init; }

    // 真透视参数：桌面平面绕 X 轴后倾 rotateX，距相机 distance，再按 fov 投影。
    // aspect 用画布真实宽高比（1200×1200 为 1.0）；near/far 只影响深度映射，不影响平面上的 x/y。
    public float PerspectiveFov { get; init; } = 55f;
    public float PerspectiveAspect { get; init; } = 1f;
    public float PerspectiveNear { get; init; } = 1f;
    public float PerspectiveFar { get; init; } = 100f;

    /// <summary>桌面平面后倾角。Mixed 用 28°（老档位 55°，太陡会把手牌顶出画布）。</summary>
    public float PerspectiveRotateX { get; init; } = 55f;

    /// <summary>相机到桌面中心的距离（逻辑像素）。越大透视越弱、越接近正交。</summary>
    public float PerspectiveDistance { get; init; } = 1010f;

    /// <summary>投影后横向等比缩放，用来把桌面铺到画布上。</summary>
    public float PerspectiveZoom { get; init; } = 1.16f;

    /// <summary>投影后的整体竖直位移（负 = 上移）。</summary>
    public float PerspectiveOffsetY { get; init; } = -282f;

    /// <summary>纵向单独缩放（0 = 跟随 <see cref="PerspectiveZoom"/>），用来补回后倾压扁的高度。</summary>
    public float PerspectiveZoomY { get; init; }

    public static readonly TableRenderMetrics Flat = new()
    {
        TileThickness = 0f,
        TableTopInset = 0f,
        TableBottomLip = 0f,
        VignetteAlpha = 0,
        EdgeHighlightAlpha = 0,
        FaceBevelAlpha = 0,
    };

    public static TableRenderMetrics For(TableRenderStyle style) => Flat;

    public bool HasTileDepth => TileThickness > 0f;
}
