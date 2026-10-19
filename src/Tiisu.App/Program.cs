using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SkiaSharp;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Ui;

namespace Tiisu.App;

internal static class Program
{
    private static readonly string AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

    [STAThread]
    private static void Main(string[] args)
    {
        // 无窗口验证：把场景渲染成 PNG（CI / 无显示器）
        if (args.Length >= 2 && args[0] == "--render")
        {
            double time = args.Length >= 3 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 0.6;
            RenderToPng(args[1], time);
            return;
        }

        // 帧率基准：离屏循环 N 帧，输出平均耗时
        if (args.Length >= 1 && args[0] == "--bench")
        {
            int frames = args.Length >= 2 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300;
            Bench(frames);
            return;
        }

        ApplicationConfiguration.Initialize();
        var atlas = new TileAtlas(AssetsDir);   // 所有权交给 MainForm（Dispose 时释放）
        Application.Run(new MainForm(atlas));
    }

    private static void RenderToPng(string path, double time)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var info = new SKImageInfo((int)BoardScene.LogicalWidth, (int)BoardScene.LogicalHeight);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            new BoardScene(atlas).Draw(ctx, time, hoverIndex: 4);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine("rendered -> " + path + "  (atlas loaded: " + atlas.IsLoaded + ")");
    }

    private static void Bench(int frames)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var info = new SKImageInfo((int)BoardScene.LogicalWidth, (int)BoardScene.LogicalHeight);
        using var surface = SKSurface.Create(info);
        var scene = new BoardScene(atlas);

        // 预热
        for (int i = 0; i < 10; i++)
        {
            using var warm = new SkiaCanvas(surface.Canvas, atlas);
            scene.Draw(warm, i * 0.016, -1);
        }

        var times = new double[frames];
        var total = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            var one = Stopwatch.StartNew();
            using var ctx = new SkiaCanvas(surface.Canvas, atlas);
            scene.Draw(ctx, i * 0.016, -1);
            one.Stop();
            times[i] = one.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        Array.Sort(times);
        double avg = total.Elapsed.TotalMilliseconds / frames;
        double p95 = times[(int)(frames * 0.95) - 1 < 0 ? 0 : (int)(frames * 0.95) - 1];
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "bench: {0} frames, avg {1:F2} ms ({2:F1} fps), p95 {3:F2} ms, atlas={4}",
            frames, avg, 1000.0 / avg, p95, atlas.IsLoaded));
    }
}
