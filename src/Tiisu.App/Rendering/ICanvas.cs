using System;
using System.Collections.Generic;

namespace Tiisu.App.Rendering;

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

/// <summary>文字样式。</summary>
public readonly record struct TextStyle(float Size, bool Bold = false, bool Center = true, bool Shadow = false);

/// <summary>
/// 渲染抽象层：Scene 只依赖这个接口。
/// 现在由 <see cref="SkiaCanvas"/> 实现；将来做 D2D 后端时新增一个实现即可，Scene 代码不动。
/// </summary>
public interface ICanvas
{
    void Save();
    void Restore();
    void Translate(float dx, float dy);
    void RotateDegrees(float degrees);
    void ClipRect(RectF r);

    void Clear(Color c);
    void FillRect(RectF r, Color c);
    void FillRoundRect(RectF r, float radius, Color c);
    void StrokeRoundRect(RectF r, float radius, Color c, float width);
    void FillLinearGradient(RectF r, float radius, Color top, Color bottom);
    void FillCircle(float cx, float cy, float radius, Color c);
    void DrawText(string text, RectF box, Color c, TextStyle style);

    /// <summary>画一张牌面（雪碧图切片）。tileId 形如 1m / 5z。</summary>
    void DrawTile(string tileId, RectF dst, float alpha = 1f);
}
