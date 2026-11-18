using System;
using System.IO;
using SkiaSharp;

namespace Tiisu.App.Rendering;

/// <summary>
/// 皮肤图片句柄：解码一次、进程内长期持有；绘制时只做采样缩放，**不做每帧解码 / resize**。
/// <para>SKImage 只出现在 Rendering 层，Scene / Table 代码看到的只是这个不透明句柄。</para>
/// </summary>
public sealed class SkinImage : IDisposable
{
    private SkinImage(string path, SKImage image)
    {
        Path = path;
        Image = image;
        Width = image.Width;
        Height = image.Height;
    }

    /// <summary>解码后的位图；<see cref="Dispose"/> 之后为 null。</summary>
    internal SKImage? Image { get; private set; }

    /// <summary>源文件绝对路径（诊断 / 热重载用）。</summary>
    public string Path { get; }

    /// <summary>源图像素宽。</summary>
    public int Width { get; }

    /// <summary>源图像素高。</summary>
    public int Height { get; }

    public bool IsValid => Image is not null && Width > 0 && Height > 0;

    /// <summary>解码一张 PNG/JPG；文件缺失、格式错误一律返回 null（调用方回退程序化绘制）。</summary>
    public static SkinImage? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var image = SKImage.FromEncodedData(path);
            return image is null ? null : new SkinImage(path, image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        Image?.Dispose();
        Image = null;
    }
}

/// <summary>
/// 九宫格边距（**源图像素**，不是逻辑像素）：四边不拉伸，中间/边条拉伸。
/// 全 0 表示"单图整体拉伸"。
/// </summary>
public readonly record struct NineSlice(float Left, float Top, float Right, float Bottom)
{
    public static readonly NineSlice None = default;

    public bool IsNone => Left <= 0f && Top <= 0f && Right <= 0f && Bottom <= 0f;
}

/// <summary>
/// 皮肤切片：图 + 源像素矩形 + 九宫格 + 缩放（源像素 / 逻辑像素，2 表示 2× 交付图）。
/// 逻辑边距 = <see cref="NineSlice"/> / <see cref="Scale"/>。
/// </summary>
public readonly record struct SkinSprite(SkinImage? Image, RectF SourcePixels, NineSlice Slice, float Scale)
{
    /// <summary>没给 scale 时按 1 处理（1 源像素 = 1 逻辑像素）。</summary>
    public float EffectiveScale => Scale > 0.01f ? Scale : 1f;

    public bool IsValid => Image is { IsValid: true } && SourcePixels.W > 0.01f && SourcePixels.H > 0.01f;

    /// <summary>这张切片在逻辑坐标下的原始尺寸。</summary>
    public float LogicalWidth => SourcePixels.W / EffectiveScale;

    public float LogicalHeight => SourcePixels.H / EffectiveScale;
}

/// <summary>
/// 能画位图的画布后端。<see cref="ICanvas"/> 保持原样不动（测试里的 NullCanvas 等不用改），
/// Skia 后端实现本接口；皮肤绘制拿不到这个接口时自动回退程序化路径。
/// </summary>
public interface IImageCanvas : ICanvas
{
    /// <summary>把图的一个子块（源像素矩形）画到逻辑矩形；<paramref name="alpha"/> 0–1。</summary>
    void DrawSkinImage(SkinImage image, RectF sourcePixels, RectF destination, float alpha = 1f);
}

/// <summary>
/// 皮肤切片绘制：九宫格 / 单图拉伸都走这里。
/// 返回 false 表示"这次没画"（没有图、后端不支持位图、目标矩形退化），调用方继续走
/// <see cref="ICanvas.FillRoundRect"/> / <see cref="ICanvas.DrawText"/> 的程序化回退。
/// </summary>
public static class SkinDraw
{
    /// <summary>按切片配置绘制：有九宫格边距走九宫格，否则整图拉伸。</summary>
    public static bool TryDraw(ICanvas c, in SkinSprite sprite, RectF dst, float alpha = 1f)
    {
        if (!sprite.IsValid || dst.W <= 0.01f || dst.H <= 0.01f || c is not IImageCanvas canvas)
        {
            return false;
        }

        if (sprite.Slice.IsNone)
        {
            canvas.DrawSkinImage(sprite.Image!, sprite.SourcePixels, dst, alpha);
            return true;
        }

        DrawNineSlice(canvas, sprite, dst, alpha);
        return true;
    }

    /// <summary>
    /// 九宫格拉伸：四角保持逻辑原始尺寸，四边与中心拉伸。
    /// 目标比角块还小时按比例压缩角块（并同步压缩采样源），避免叠加。
    /// </summary>
    private static void DrawNineSlice(IImageCanvas canvas, in SkinSprite sprite, RectF dst, float alpha)
    {
        var s = sprite.SourcePixels;
        var slice = sprite.Slice;
        float scale = sprite.EffectiveScale;

        float left = slice.Left / scale;
        float top = slice.Top / scale;
        float right = slice.Right / scale;
        float bottom = slice.Bottom / scale;

        // 目标太小：角块按比例收缩（源采样同步收缩，保证采样区不越界、不错位）。
        float sumX = left + right;
        if (sumX > dst.W)
        {
            float k = dst.W / sumX;
            left *= k;
            right *= k;
        }

        float sumY = top + bottom;
        if (sumY > dst.H)
        {
            float k = dst.H / sumY;
            top *= k;
            bottom *= k;
        }

        float srcL = left * scale;
        float srcT = top * scale;
        float srcR = right * scale;
        float srcB = bottom * scale;

        float sx0 = s.X;
        float sx1 = s.X + srcL;
        float sx2 = s.Right - srcR;
        float sx3 = s.Right;
        float sy0 = s.Y;
        float sy1 = s.Y + srcT;
        float sy2 = s.Bottom - srcB;
        float sy3 = s.Bottom;

        float dx0 = dst.X;
        float dx1 = dst.X + left;
        float dx2 = dst.Right - right;
        float dx3 = dst.Right;
        float dy0 = dst.Y;
        float dy1 = dst.Y + top;
        float dy2 = dst.Bottom - bottom;
        float dy3 = dst.Bottom;

        float SrcX(int i) => i switch
        {
            0 => sx0,
            1 => sx1,
            2 => sx2,
            _ => sx3,
        };

        float SrcY(int i) => i switch
        {
            0 => sy0,
            1 => sy1,
            2 => sy2,
            _ => sy3,
        };

        float DstX(int i) => i switch
        {
            0 => dx0,
            1 => dx1,
            2 => dx2,
            _ => dx3,
        };

        float DstY(int i) => i switch
        {
            0 => dy0,
            1 => dy1,
            2 => dy2,
            _ => dy3,
        };

        for (int row = 0; row < 3; row++)
        {
            float sy = SrcY(row);
            float shy = SrcY(row + 1) - sy;
            float dy = DstY(row);
            float dhy = DstY(row + 1) - dy;
            if (shy <= 0.01f || dhy <= 0.01f)
            {
                continue;
            }

            for (int col = 0; col < 3; col++)
            {
                float sx = SrcX(col);
                float shx = SrcX(col + 1) - sx;
                float dx = DstX(col);
                float dhx = DstX(col + 1) - dx;
                if (shx <= 0.01f || dhx <= 0.01f)
                {
                    continue;
                }

                canvas.DrawSkinImage(sprite.Image!, new RectF(sx, sy, shx, shy), new RectF(dx, dy, dhx, dhy), alpha);
            }
        }
    }
}
