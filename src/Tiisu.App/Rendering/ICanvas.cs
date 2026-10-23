using System;
using System.Collections.Generic;

namespace Tiisu.App.Rendering;

/// <summary>绘制用点（逻辑坐标，左上原点）。</summary>
public readonly record struct CanvasPoint(float X, float Y);

/// <summary>
/// 真透视相机参数（逻辑像素）：桌面平面绕 X 轴后倾 <see cref="RotateXDegrees"/>，
/// 距相机 <see cref="Distance"/>，按 <see cref="FovDegrees"/> 投影到
/// <see cref="CanvasWidth"/>×<see cref="CanvasHeight"/> 的画布。
/// 只用于桌面图层；牌与命中测试仍在逻辑坐标里。
/// </summary>
public readonly record struct PerspectiveCamera(
    float FovDegrees,
    float Aspect,
    float Near,
    float Far,
    float RotateXDegrees,
    float Distance,
    float CanvasWidth,
    float CanvasHeight,
    float Zoom = 1f,
    float OffsetY = 0f);

/// <summary>绘制用矩形（逻辑坐标，左上原点）。</summary>
public readonly record struct RectF(float X, float Y, float W, float H)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + W;
    public float Bottom => Y + H;
    public float CenterX => X + W / 2f;
    public float CenterY => Y + H / 2f;
    public static RectF FromLTRB(float l, float t, float r, float b) => new(l, t, r - l, b - t);
    public RectF Inflate(float d) => new(X - d, Y - d, W + d * 2, H + d * 2);
    public bool Contains(float px, float py) => px >= X && px <= Right && py >= Y && py <= Bottom;
}

public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static Color Rgb(int r, int g, int b) => new((byte)r, (byte)g, (byte)b);
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Transparent = new(0, 0, 0, 0);
    public Color WithAlpha(byte a) => this with { A = a };
}

/// <summary>文字样式。Right 优先于 Center（用于数字右对齐的列）。</summary>
public readonly record struct TextStyle(float Size, bool Bold = false, bool Center = true, bool Shadow = false, bool Right = false);

/// <summary>
/// 渲染抽象层：Scene 只依赖这个接口。
/// 现在由 <see cref="SkiaCanvas"/> 实现；将来做 D2D 后端时新增一个实现即可，Scene 代码不动。
/// </summary>
public interface ICanvas
{
    void Save();
    void Restore();
    void Translate(float dx, float dy);
    void Scale(float factor);
    void RotateDegrees(float degrees);
    void ClipRect(RectF r);

    /// <summary>
    /// 把当前坐标系乘以真透视矩阵（不是梯形近似）。只在 <see cref="Save"/> / <see cref="Restore"/>
    /// 之间用于桌面图层；恢复后牌仍按逻辑坐标绘制。
    /// </summary>
    void ConcatPerspective(in PerspectiveCamera camera);

    void Clear(Color c);
    void FillRect(RectF r, Color c);
    void FillRoundRect(RectF r, float radius, Color c);
    void StrokeRoundRect(RectF r, float radius, Color c, float width);

    /// <summary>用 top→bottom 的竖直渐变填充矩形；<paramref name="horizontal"/> = true 时改为 left→right。</summary>
    void FillLinearGradient(RectF r, float radius, Color top, Color bottom, bool horizontal = false);

    /// <summary>填充任意四边形/多边形（顶点顺序顺/逆时针均可）。</summary>
    void FillPolygon(IReadOnlyList<CanvasPoint> points, Color c);

    /// <summary>用上→下的竖直渐变填充多边形（桌面梯形用）。</summary>
    void FillPolygonGradient(IReadOnlyList<CanvasPoint> points, Color top, Color bottom);

    /// <summary>描边多边形。</summary>
    void StrokePolygon(IReadOnlyList<CanvasPoint> points, Color c, float width);

    /// <summary>带模糊的投影（牌身下方）。<paramref name="blur"/> 为 0 时等同实心圆角矩形。</summary>
    void FillShadow(RectF r, float radius, float blur, Color c);

    void FillCircle(float cx, float cy, float radius, Color c);
    void DrawText(string text, RectF box, Color c, TextStyle style);

    /// <summary>量一段文字的宽度（与 <see cref="DrawText"/> 同一字体/字号），用于可点击文字排版。</summary>
    float MeasureText(string text, TextStyle style);

    /// <summary>画一张牌面（雪碧图切片）。tileId 形如 1m / 5z。</summary>
    void DrawTile(string tileId, RectF dst, float alpha = 1f);
}
