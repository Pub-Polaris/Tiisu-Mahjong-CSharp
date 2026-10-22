using System;
using System.Collections.Generic;
using SkiaSharp;
using Tiisu.App.Assets;

namespace Tiisu.App.Rendering;

/// <summary>ICanvas 的 Skia 实现。SKCanvas 只在这里出现，Scene 看不到 SkiaSharp 类型。</summary>
public sealed class SkiaCanvas : ICanvas, IDisposable
{
    private static readonly Dictionary<(string Family, bool Bold), SKTypeface> Typefaces = new();

    private SKCanvas _canvas;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

    private readonly TileAtlas? _atlas;

    public SkiaCanvas(SKCanvas canvas, TileAtlas? atlas = null)
    {
        _canvas = canvas;
        _atlas = atlas;
    }

    public void Rebind(SKCanvas canvas) => _canvas = canvas;

    public void Save() => _canvas.Save();
    public void Restore() => _canvas.Restore();
    public void Translate(float dx, float dy) => _canvas.Translate(dx, dy);
    public void RotateDegrees(float degrees) => _canvas.RotateDegrees(degrees);
    public void ClipRect(RectF r) => _canvas.ClipRect(new SKRect(r.X, r.Y, r.Right, r.Bottom));

    private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

    public void Clear(Color c) => _canvas.Clear(ToSk(c));

    public void FillRect(RectF r, Color c)
    {
        _fill.Color = ToSk(c);
        _fill.Shader = null;
        _canvas.DrawRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), _fill);
    }

    public void FillRoundRect(RectF r, float radius, Color c)
    {
        _fill.Color = ToSk(c);
        _fill.Shader = null;
        _canvas.DrawRoundRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), radius, radius, _fill);
    }

    public void StrokeRoundRect(RectF r, float radius, Color c, float width)
    {
        _stroke.Color = ToSk(c);
        _stroke.StrokeWidth = width;
        _stroke.Shader = null;
        _canvas.DrawRoundRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), radius, radius, _stroke);
    }

    public void FillLinearGradient(RectF r, float radius, Color top, Color bottom)
    {
        var rect = new SKRect(r.X, r.Y, r.Right, r.Bottom);
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Left, rect.Bottom),
            new[] { ToSk(top), ToSk(bottom) },
            null,
            SKShaderTileMode.Clamp);
        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        _canvas.DrawRoundRect(rect, radius, radius, _fill);
        _fill.Shader = null;
    }

    public void FillCircle(float cx, float cy, float radius, Color c)
    {
        _fill.Color = ToSk(c);
        _fill.Shader = null;
        _canvas.DrawCircle(cx, cy, radius, _fill);
    }

    /// <summary>
    /// SkiaSharp 3/4 起文字不再挂在 SKPaint 上：字体信息走 SKFont，DrawText 显式收对齐方式。
    /// </summary>
    public void DrawText(string text, RectF box, Color c, TextStyle style)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = ToSk(c),
            MaskFilter = null,
        };
        using var font = new SKFont(GetTypeface(style.Bold), style.Size);
        var align = style.Right ? SKTextAlign.Right : style.Center ? SKTextAlign.Center : SKTextAlign.Left;

        var metrics = font.Metrics;
        float baseline = box.Y + (box.H - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;
        float x = style.Right ? box.Right : style.Center ? box.CenterX : box.X;

        if (style.Shadow)
        {
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 0, 0, 140),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f),
            };
            _canvas.DrawText(text, x + 1.5f, baseline + 1.5f, align, font, shadowPaint);
        }

        _canvas.DrawText(text, x, baseline, align, font, paint);
    }

    private static SKTypeface GetTypeface(bool bold)
    {
        const string family = "Microsoft YaHei";
        var key = (family, bold);
        if (Typefaces.TryGetValue(key, out var tf) && tf is not null)
        {
            return tf;
        }

        tf = SKTypeface.FromFamilyName(family,
                 bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                 SKFontStyleWidth.Normal,
                 SKFontStyleSlant.Upright)
             ?? SKTypeface.Default;
        Typefaces[key] = tf;
        return tf;
    }

    public void DrawTile(string tileId, RectF dst, float alpha = 1f)
    {
        if (_atlas is { IsLoaded: true } atlas && atlas.Sheet is { } sheet
            && atlas.TryGetSource(tileId, out var src))
        {
            using var paint = new SKPaint { IsAntialias = true };
            paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(alpha * 255f, 0f, 255f));
            var d = new SKRect(dst.X, dst.Y, dst.Right, dst.Bottom);
            _canvas.DrawImage(sheet, src, d, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }
    }

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
    }
}
