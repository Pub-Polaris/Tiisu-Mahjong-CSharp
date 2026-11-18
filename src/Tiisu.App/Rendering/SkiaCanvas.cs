using System;
using System.Collections.Generic;
using SkiaSharp;
using Tiisu.App.Assets;

namespace Tiisu.App.Rendering;

/// <summary>ICanvas 的 Skia 实现。SKCanvas 只在这里出现，Scene 看不到 SkiaSharp 类型。</summary>
public sealed class SkiaCanvas : IImageCanvas, IDisposable
{
    private static readonly Dictionary<(string Family, bool Bold), SKTypeface> Typefaces = new();

    private SKCanvas _canvas;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _image = new() { IsAntialias = true };

    // 每帧复用：字体按 (粗体, 字号) 缓存；透视矩阵只在相机参数变化时重建（P2-5）。
    private readonly Dictionary<(bool Bold, float Size, string Family), SKFont> _fonts = new();

    /// <summary>非牌桌页面的大字号倍率；1 = 默认。</summary>
    public float TextScale { get; set; } = 1f;
    private SKMatrix44 _perspective;
    private PerspectiveCamera _perspectiveCamera;
    private bool _perspectiveSet;

    private readonly TileAtlas? _atlas;

    public SkiaCanvas(SKCanvas canvas, TileAtlas? atlas = null)
    {
        _canvas = canvas;
        _atlas = atlas;
    }

    public void Rebind(SKCanvas canvas) => _canvas = canvas;

    /// <summary>当前画布加载的牌面雪碧图（结算缩略图需要另开离屏 surface 时复用）。</summary>
    public TileAtlas? Atlas => _atlas;

    public void Save() => _canvas.Save();
    public void Restore() => _canvas.Restore();
    public void Translate(float dx, float dy) => _canvas.Translate(dx, dy);
    public void Scale(float factor) => _canvas.Scale(factor);
    public void RotateDegrees(float degrees) => _canvas.RotateDegrees(degrees);
    public void ClipRect(RectF r) => _canvas.ClipRect(new SKRect(r.X, r.Y, r.Right, r.Bottom));

    /// <summary>
    /// 皮肤切片：把源像素矩形画到逻辑矩形。图片是进程内长期持有的 <see cref="SKImage"/>，
    /// 每次只做采样缩放，不解码、不 resize。
    /// </summary>
    public void DrawSkinImage(SkinImage image, RectF sourcePixels, RectF destination, float alpha = 1f)
    {
        if (image.Image is not { } sheet || destination.W <= 0f || destination.H <= 0f)
        {
            return;
        }

        _image.Color = SKColors.White.WithAlpha((byte)Math.Clamp(alpha * 255f, 0f, 255f));
        _canvas.DrawImage(
            sheet,
            new SKRect(sourcePixels.X, sourcePixels.Y, sourcePixels.Right, sourcePixels.Bottom),
            new SKRect(destination.X, destination.Y, destination.Right, destination.Bottom),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
            _image);
    }

    /// <summary>静态层录制：临时把绘制目标换成离屏 surface 的画布，返回原画布。</summary>
    internal SKCanvas SwapBackingCanvas(SKCanvas next)
    {
        SKCanvas previous = _canvas;
        _canvas = next;
        return previous;
    }

    /// <summary>
    /// 当前设备缩放（宿主 letterbox 缩放后「每逻辑像素 = 多少物理像素」）。
    /// 静态层按这个缩放预合成，回放时才能落在同一设备像素网格上。
    /// </summary>
    internal float DeviceScale
    {
        get
        {
            SKMatrix matrix = _canvas.TotalMatrix;
            float scale = MathF.Max(MathF.Abs(matrix.ScaleX), MathF.Abs(matrix.ScaleY));
            return scale > 0.01f ? scale : 1f;
        }
    }

    /// <summary>当前「逻辑 → 设备像素」矩阵（静态层按它把内容合成到同一像素网格）。</summary>
    internal SKMatrix TotalMatrix => _canvas.TotalMatrix;

    /// <summary>
    /// 回放预合成的静态层：位图是在同一设备像素网格上合成的，这里按整数设备坐标 1:1 贴上，
    /// 不缩放、不插值（像素相位与直接绘制一致）。
    /// </summary>
    internal void DrawCachedLayer(SKImage image, int deviceX, int deviceY)
    {
        _image.Color = SKColors.White;
        _canvas.Save();
        _canvas.SetMatrix(new SKMatrix(
            1f, 0f, deviceX,
            0f, 1f, deviceY,
            0f, 0f, 1f));
        _canvas.DrawImage(
            image,
            new SKRect(0f, 0f, image.Width, image.Height),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            _image);
        _canvas.Restore();
    }

    /// <summary>
    /// 真透视：M = V · P · T(0,0,-distance) · R_x(-rotateX) · T(-cx,-cy)。
    /// 桌面平面（z=0）经绕 X 轴后倾、透视投影、视口缩放后落到画布上；
    /// 不是把矩形四角手动收窄的梯形近似。
    /// </summary>
    public void ConcatPerspective(in PerspectiveCamera camera)
    {
        if (!_perspectiveSet || !_perspectiveCamera.Equals(camera))
        {
            _perspective = BuildPerspective(camera);
            _perspectiveCamera = camera;
            _perspectiveSet = true;
        }

        _canvas.Concat(in _perspective);
    }

    /// <summary>
    /// 直接把 4×4 透视矩阵写成 SKMatrix44。SkiaSharp 4.153.1 没有
    /// <c>SKMatrix44.CreatePerspective</c>，这里按同一套标准投影公式构造：
    /// P 的 x/y 缩放 = cot(fov/2)，w = -z；再把旋转、相机距离和视口缩放乘进去。
    /// SKMatrix44 的索引器是 [列, 行]。
    /// </summary>
    private static SKMatrix44 BuildPerspective(in PerspectiveCamera cam)
    {
        float halfFov = cam.FovDegrees * MathF.PI / 360f;
        float cot = 1f / MathF.Tan(halfFov);
        float sin = MathF.Sin(cam.RotateXDegrees * MathF.PI / 180f);
        float cos = MathF.Cos(cam.RotateXDegrees * MathF.PI / 180f);

        float cx = cam.CanvasWidth / 2f;
        float cy = cam.CanvasHeight / 2f;
        float zoomY = cam.ZoomY > 0f ? cam.ZoomY : cam.Zoom;
        float kx = (cot / cam.Aspect) * (cam.CanvasWidth / 2f) * cam.Zoom;
        float ky = cot * (cam.CanvasHeight / 2f) * zoomY;

        // 标准 OpenGL 投影的 z 行；只影响深度，不影响桌面平面上的 x/y。
        float zScale = (cam.Far + cam.Near) / (cam.Near - cam.Far);
        float zShift = 2f * cam.Far * cam.Near / (cam.Near - cam.Far);

        var m = new SKMatrix44();

        // 行 0（x）
        m[0, 0] = kx;
        m[1, 0] = -cx * sin;
        m[3, 0] = cx * (cam.Distance + sin * cy - kx);

        // 行 1（y）
        m[1, 1] = ky * cos - (cy + cam.OffsetY) * sin;
        m[3, 1] = (cy + cam.OffsetY) * (cam.Distance + sin * cy) - ky * cos * cy;

        // 行 2（z）
        m[2, 2] = zScale;
        m[3, 2] = zShift;

        // 行 3（w）：近边（画布下方）w 更小 → 更靠近相机。
        m[1, 3] = -sin;
        m[3, 3] = cam.Distance + sin * cy;

        return m;
    }

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

    public void FillLinearGradient(RectF r, float radius, Color top, Color bottom, bool horizontal = false)
    {
        var rect = new SKRect(r.X, r.Y, r.Right, r.Bottom);
        var start = new SKPoint(rect.Left, rect.Top);
        var end = horizontal ? new SKPoint(rect.Right, rect.Top) : new SKPoint(rect.Left, rect.Bottom);
        using var shader = SKShader.CreateLinearGradient(
            start,
            end,
            new[] { ToSk(top), ToSk(bottom) },
            null,
            SKShaderTileMode.Clamp);
        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        _canvas.DrawRoundRect(rect, radius, radius, _fill);
        _fill.Shader = null;
    }

    private static SKPath BuildPath(IReadOnlyList<CanvasPoint> points)
    {
        using var builder = new SKPathBuilder();
        if (points.Count > 0)
        {
            builder.MoveTo(points[0].X, points[0].Y);
            for (int i = 1; i < points.Count; i++)
            {
                builder.LineTo(points[i].X, points[i].Y);
            }

            builder.Close();
        }

        return builder.Detach();
    }

    public void FillPolygon(IReadOnlyList<CanvasPoint> points, Color c)
    {
        using var path = BuildPath(points);
        _fill.Color = ToSk(c);
        _fill.Shader = null;
        _canvas.DrawPath(path, _fill);
    }

    public void FillPolygonGradient(IReadOnlyList<CanvasPoint> points, Color top, Color bottom)
    {
        if (points.Count == 0)
        {
            return;
        }

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < points.Count; i++)
        {
            minY = Math.Min(minY, points[i].Y);
            maxY = Math.Max(maxY, points[i].Y);
        }

        if (Math.Abs(maxY - minY) < 0.01f)
        {
            FillPolygon(points, top);
            return;
        }

        using var path = BuildPath(points);
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0f, minY),
            new SKPoint(0f, maxY),
            new[] { ToSk(top), ToSk(bottom) },
            null,
            SKShaderTileMode.Clamp);
        _fill.Shader = shader;
        _fill.Color = SKColors.White;
        _canvas.DrawPath(path, _fill);
        _fill.Shader = null;
    }

    public void StrokePolygon(IReadOnlyList<CanvasPoint> points, Color c, float width)
    {
        using var path = BuildPath(points);
        _stroke.Color = ToSk(c);
        _stroke.StrokeWidth = width;
        _stroke.Shader = null;
        _canvas.DrawPath(path, _stroke);
    }

    public void FillShadow(RectF r, float radius, float blur, Color c)
    {
        var rect = new SKRect(r.X, r.Y, r.Right, r.Bottom);
        using var paint = new SKPaint { IsAntialias = true, Color = ToSk(c) };
        if (blur > 0.01f)
        {
            using var mask = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur);
            paint.MaskFilter = mask;
            _canvas.DrawRoundRect(rect, radius, radius, paint);
            return;
        }

        _canvas.DrawRoundRect(rect, radius, radius, paint);
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
        var font = GetFont(style.Bold, BoostSize(style.Size) * Math.Clamp(TextScale, 0.8f, 1.5f), style.Family);
        var align = style.Right ? SKTextAlign.Right : style.Center ? SKTextAlign.Center : SKTextAlign.Left;

        var metrics = font.Metrics;
        float baseline = box.Y + (box.H - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;
        float x = style.Right ? box.Right : style.Center ? box.CenterX : box.X;

        if (RubyText.HasMarkup(text))
        {
            DrawRubyText(text, box, style, paint, font, align, baseline);
            return;
        }

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

    private void DrawRubyText(string text, RectF box, TextStyle style, SKPaint paint,
        SKFont font, SKTextAlign align, float baseline)
    {
        var segments = RubyText.Parse(text);
        float total = 0f;
        foreach (var segment in segments)
        {
            total += font.MeasureText(segment.Base);
        }

        using var rubyFont = new SKFont(GetTypeface(style.Bold, style.Family),
            BoostSize(style.Size) * Math.Clamp(TextScale, 0.8f, 1.5f) * 0.42f);
        using SKPaint? shadowPaint = style.Shadow
            ? new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 0, 0, 140),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f),
            }
            : null;

        float cursor = align == SKTextAlign.Right ? box.Right - total
            : align == SKTextAlign.Center ? box.CenterX - total / 2f
            : box.X;
        float rubyBaseline = baseline + font.Metrics.Ascent - 1.5f;
        foreach (var segment in segments)
        {
            float baseWidth = font.MeasureText(segment.Base);
            if (shadowPaint is not null)
            {
                _canvas.DrawText(segment.Base, cursor + 1.5f, baseline + 1.5f,
                    SKTextAlign.Left, font, shadowPaint);
            }
            _canvas.DrawText(segment.Base, cursor, baseline, SKTextAlign.Left, font, paint);
            if (!string.IsNullOrEmpty(segment.Ruby))
            {
                float rubyWidth = rubyFont.MeasureText(segment.Ruby);
                float rubyX = cursor + (baseWidth - rubyWidth) / 2f;
                if (shadowPaint is not null)
                {
                    _canvas.DrawText(segment.Ruby, rubyX + 1.5f, rubyBaseline + 1.5f,
                        SKTextAlign.Left, rubyFont, shadowPaint);
                }
                _canvas.DrawText(segment.Ruby, rubyX, rubyBaseline, SKTextAlign.Left, rubyFont, paint);
            }

            cursor += baseWidth;
        }
    }

    /// <summary>量文字宽度：与 DrawText 共用同一 typeface / 字号（含 BoostSize），保证点击区与字形对齐。</summary>
    public float MeasureText(string text, TextStyle style) => MeasureScaled(RubyText.StripMarkup(text), style);

    private float MeasureScaled(string text, TextStyle style)
    {
        using var font = new SKFont(GetTypeface(style.Bold, style.Family), BoostSize(style.Size) * Math.Clamp(TextScale, 0.8f, 1.5f));
        return font.MeasureText(text);
    }

    /// <summary>无 canvas 的静态测量（命中测试、署名片段排版用）。</summary>
    public static float Measure(string text, TextStyle style)
    {
        using var font = new SKFont(GetTypeface(style.Bold, style.Family), BoostSize(style.Size));
        return font.MeasureText(RubyText.StripMarkup(text));
    }

    /// <summary>字号整体放大一档：小字 +2、中字 +3、大字 +4。</summary>
    private static float BoostSize(float size) => size < 16f ? size + 2f : size < 24f ? size + 3f : size + 4f;

    private const string SystemFamily = "Microsoft YaHei";
    private const string DefaultFamily = "Tiisu Rounded SC";
    private const string RoundedFamily = "Tiisu Rounded SC";
    private static SKTypeface? _roundedBase;

    private static SKTypeface GetTypeface(bool bold, string? family = null)
    {
        string fam = string.IsNullOrWhiteSpace(family) ? DefaultFamily : family;
        var key = (fam, bold);
        if (Typefaces.TryGetValue(key, out var tf) && tf is not null)
        {
            return tf;
        }

        if (fam == RoundedFamily)
        {
            tf = LoadRounded(bold);
            if (tf is null)
            {
                fam = SystemFamily;
            }
        }

        tf ??= SKTypeface.FromFamilyName(fam,
                   bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                   SKFontStyleWidth.Normal,
                   SKFontStyleSlant.Upright)
               ?? SKTypeface.Default;
        Typefaces[key] = tf;
        return tf;
    }

    /// <summary>嵌入的文渊圆体可变字重：wght 400/700，失败回退系统字体。</summary>
    private static SKTypeface? LoadRounded(bool bold)
    {
        try
        {
            _roundedBase ??= SKTypeface.FromFile(Path.Combine(
                AppContext.BaseDirectory, "Assets", "fonts", "TiisuRoundedSC-subset.ttf"));
            if (_roundedBase is null)
            {
                return null;
            }

            var args = new SKFontArguments
            {
                VariationDesignPosition = new SKFontVariationPositionCoordinate[]
                {
                    new() { Axis = SKFourByteTag.Parse("wght"), Value = bold ? 700 : 400 },
                },
            };
            return _roundedBase.Clone(args);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>字体按 (粗体, 字号, 字体族) 缓存，每帧不再 new SKFont（P2-5）。</summary>
    private SKFont GetFont(bool bold, float size, string? family = null)
    {
        string fam = string.IsNullOrWhiteSpace(family) ? DefaultFamily : family;
        var key = (bold, size, fam);
        if (_fonts.TryGetValue(key, out SKFont? font))
        {
            return font;
        }

        font = new SKFont(GetTypeface(bold, family), size);
        _fonts[key] = font;
        return font;
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

    /// <summary>把一张离屏渲染好的位图（结算缩略图）按目标矩形画上。仅 Skia 后端需要。</summary>
    public void DrawImage(SKImage image, RectF dst)
    {
        var d = new SKRect(dst.X, dst.Y, dst.Right, dst.Bottom);
        using var paint = new SKPaint { IsAntialias = true };
        _canvas.DrawImage(image, d, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
    }

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _image.Dispose();
        foreach (SKFont font in _fonts.Values)
        {
            font.Dispose();
        }

        _fonts.Clear();
        _perspectiveSet = false;
    }
}
