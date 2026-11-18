using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Tiisu.App.Rendering;

/// <summary>
/// 静态层缓存：把「每帧都不变的一层」（桌面底色 / 边框 / 中央板底 / 牌河底 / 阴影）
/// 预合成到一张离屏位图（SKImage），之后每帧只画这一张图；动态牌、文字、按钮状态照常实时绘制。
///
/// 合成用的是**当前设备像素网格**（含窗口 letterbox 的整数偏移与缩放），所以回放是一次
/// 无缩放的位块拷贝：像素相位与直接绘制完全一致，不引入插值/半像素偏移。
/// 这正是皮肤模式下把「几十次带插值的图片绘制」压成一次 blit 的关键。
///
/// 键由调用方定义（尺寸 / 布局矩形 / 观感 / 皮肤版本），再叠加当前设备变换：
/// 尺寸 / 布局 / 主题 / 换皮 / 窗口缩放变化时自然重建；条目数封顶，防止拖动窗口时无限增长。
///
/// 非 Skia 后端（测试里的 NullCanvas 等）直接调用 <c>build</c>，不缓存、不改变行为。
/// </summary>
/// <typeparam name="TKey">静态层指纹；必须是值类型，避免每帧装箱分配。</typeparam>
public static class StaticLayerCache<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>同时缓存的静态层上限（位图按设备像素，每张几 MB）。</summary>
    private const int Capacity = 2;

    private static readonly object Gate = new();
    private static readonly Dictionary<(TKey Key, int X, int Y, float Scale), Entry> Entries = new();
    private static readonly List<(TKey Key, int X, int Y, float Scale)> Order = new(Capacity);

    private static long _hits;
    private static long _misses;

    /// <summary>总开关：<c>TIISU_STATIC_LAYER=off</c> 时关闭（性能对照 / 排障用）。</summary>
    public static bool Enabled { get; } = DetectEnabled();

    /// <summary>当前缓存的静态层数量。</summary>
    public static int Count
    {
        get
        {
            lock (Gate)
            {
                return Entries.Count;
            }
        }
    }

    /// <summary>命中 / 未命中计数（诊断用；重建应只发生在尺寸/布局/主题变化时）。</summary>
    public static (long Hits, long Misses) Stats
    {
        get
        {
            lock (Gate)
            {
                return (_hits, _misses);
            }
        }
    }

    /// <summary>
    /// 只查缓存并回放：命中返回 true（每帧热路径，不分配委托）。
    /// 未命中 / 后端不支持 / 总开关关闭时返回 false，调用方再调 <see cref="Draw"/> 预合成。
    /// </summary>
    public static bool TryDraw(ICanvas c, in TKey key, RectF bounds)
    {
        if (!Enabled || c is not SkiaCanvas skia)
        {
            return false;
        }

        DeviceGeometry geometry = DeviceGeometry.From(skia, bounds);
        if (!geometry.IsValid)
        {
            return false;
        }

        var lookup = (key, geometry.OriginX, geometry.OriginY, geometry.Scale);
        if (!TryGet(lookup, out Entry entry))
        {
            return false;
        }

        lock (Gate)
        {
            _hits++;
        }

        skia.DrawCachedLayer(entry.Image, entry.OriginX, entry.OriginY);
        return true;
    }

    /// <summary>
    /// 预合成一层静态内容并立即画上；之后同键同设备变换的帧走 <see cref="TryDraw"/> 的热路径。
    /// <paramref name="bounds"/> 是内容的逻辑外接矩形（一般是整张画布）。
    /// </summary>
    public static void Draw(ICanvas c, in TKey key, RectF bounds, Action<ICanvas> build)
    {
        if (!Enabled || c is not SkiaCanvas skia)
        {
            build(c);
            return;
        }

        DeviceGeometry geometry = DeviceGeometry.From(skia, bounds);
        if (!geometry.IsValid)
        {
            build(c);
            return;
        }

        var lookup = (key, geometry.OriginX, geometry.OriginY, geometry.Scale);
        if (TryGet(lookup, out Entry cached))
        {
            lock (Gate)
            {
                _hits++;
            }

            skia.DrawCachedLayer(cached.Image, cached.OriginX, cached.OriginY);
            return;
        }

        Entry? rendered = Render(skia, c, geometry, build);
        if (rendered is not { } entry)
        {
            return; // 极端尺寸拿不到离屏 surface：已经按 build 直接画过，这一帧不进缓存。
        }

        lock (Gate)
        {
            _misses++;
            if (Entries.Count >= Capacity && Order.Count > 0)
            {
                var oldest = Order[0];
                Order.RemoveAt(0);
                if (Entries.Remove(oldest, out Entry stale))
                {
                    stale.Image.Dispose();
                }
            }

            Entries[lookup] = entry;
            Order.Add(lookup);
        }

        skia.DrawCachedLayer(entry.Image, entry.OriginX, entry.OriginY);
    }

    /// <summary>清空缓存（换皮 / 热重载 / 后端销毁时调用，避免画到已释放的图）。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            foreach (Entry entry in Entries.Values)
            {
                entry.Image.Dispose();
            }

            Entries.Clear();
            Order.Clear();
            _hits = 0;
            _misses = 0;
        }
    }

    private static bool TryGet(in (TKey Key, int X, int Y, float Scale) lookup, out Entry entry)
    {
        lock (Gate)
        {
            return Entries.TryGetValue(lookup, out entry);
        }
    }

    /// <summary>
    /// 在与当前画布**同一设备像素网格**的离屏 surface 上画一遍静态层，快照成 SKImage。
    /// 之后回放时把图按整数设备坐标直接贴上去——像素相位与直接绘制一致。
    /// </summary>
    private static Entry? Render(SkiaCanvas skia, ICanvas c, in DeviceGeometry geometry, Action<ICanvas> build)
    {
        if (geometry.PixelW <= 0 || geometry.PixelH <= 0)
        {
            build(c);
            return null;
        }

        using var surface = SKSurface.Create(new SKImageInfo(geometry.PixelW, geometry.PixelH));
        if (surface is null)
        {
            // 极端尺寸下拿不到 surface：直接画，不进缓存。
            build(c);
            return null;
        }

        SKCanvas recordCanvas = surface.Canvas;
        recordCanvas.Clear(SKColors.Transparent);
        recordCanvas.SetMatrix(geometry.Matrix);

        SKCanvas previous = skia.SwapBackingCanvas(recordCanvas);
        try
        {
            build(c);
        }
        finally
        {
            skia.SwapBackingCanvas(previous);
        }

        return new Entry(surface.Snapshot(), geometry.OriginX, geometry.OriginY);
    }

    private static bool DetectEnabled()
    {
        try
        {
            string? value = Environment.GetEnvironmentVariable("TIISU_STATIC_LAYER");
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return value.Trim().ToLowerInvariant() is not ("off" or "0" or "false" or "none");
        }
        catch
        {
            return true;
        }
    }

    /// <summary>把逻辑外接矩形映射到设备像素，取其整数外包 + 等价矩阵（去掉整数平移）。</summary>
    private readonly struct DeviceGeometry
    {
        private DeviceGeometry(bool valid, SKMatrix matrix, int originX, int originY, int pixelW, int pixelH, float scale)
        {
            IsValid = valid;
            Matrix = matrix;
            OriginX = originX;
            OriginY = originY;
            PixelW = pixelW;
            PixelH = pixelH;
            Scale = scale;
        }

        public bool IsValid { get; }

        /// <summary>逻辑坐标 → 本 surface 像素坐标的矩阵（= 画布矩阵去掉整数平移）。</summary>
        public SKMatrix Matrix { get; }

        /// <summary>本 surface 左上角在设备像素里的坐标。</summary>
        public int OriginX { get; }

        public int OriginY { get; }

        public int PixelW { get; }

        public int PixelH { get; }

        /// <summary>设备缩放（近似，仅用于缓存键区分；实际相位由整数原点 + 矩阵决定）。</summary>
        public float Scale { get; }

        public static DeviceGeometry From(SkiaCanvas skia, RectF bounds)
        {
            SKMatrix total = skia.TotalMatrix;
            float scale = skia.DeviceScale;

            // 四个角一起变换（宿主只有缩放+平移，这里仍按通用情况取外包，稳健）。
            SKPoint p0 = total.MapPoint(bounds.X, bounds.Y);
            SKPoint p1 = total.MapPoint(bounds.Right, bounds.Y);
            SKPoint p2 = total.MapPoint(bounds.Right, bounds.Bottom);
            SKPoint p3 = total.MapPoint(bounds.X, bounds.Bottom);

            float minX = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
            float maxX = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
            float minY = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
            float maxY = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));

            if (!float.IsFinite(minX) || !float.IsFinite(maxY) || maxX <= minX || maxY <= minY)
            {
                return default;
            }

            int originX = (int)MathF.Floor(minX);
            int originY = (int)MathF.Floor(minY);
            int pixelW = (int)MathF.Ceiling(maxX) - originX;
            int pixelH = (int)MathF.Ceiling(maxY) - originY;
            if (pixelW <= 0 || pixelH <= 0 || pixelW > 8192 || pixelH > 8192)
            {
                return default;
            }

            // M' = T(-origin) ∘ M：仿射矩阵只改平移项。
            var matrix = new SKMatrix(
                total.ScaleX, total.SkewX, total.TransX - originX,
                total.SkewY, total.ScaleY, total.TransY - originY,
                total.Persp0, total.Persp1, total.Persp2);

            return new DeviceGeometry(true, matrix, originX, originY, pixelW, pixelH, scale);
        }
    }

    private readonly struct Entry
    {
        public Entry(SKImage image, int originX, int originY)
        {
            Image = image;
            OriginX = originX;
            OriginY = originY;
        }

        public SKImage Image { get; }

        public int OriginX { get; }

        public int OriginY { get; }
    }
}
