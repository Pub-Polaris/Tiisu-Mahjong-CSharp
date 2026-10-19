using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SkiaSharp;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Ui;
using UiScreen = Tiisu.App.Ui.Screen;

namespace Tiisu.App;

internal static class Program
{
    private static readonly string AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

    [STAThread]
    private static void Main(string[] args)
    {
        // 无窗口验证：--render <png> [time] [table|menu]
        if (args.Length >= 2 && args[0] == "--render")
        {
            double time = args.Length >= 3 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 0.6;
            string screen = args.Length >= 4 ? args[3] : "table";
            RenderToPng(args[1], time, screen);
            return;
        }

        // 帧率基准：--bench <frames>
        if (args.Length >= 1 && args[0] == "--bench")
        {
            int frames = args.Length >= 2 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300;
            Bench(frames);
            return;
        }

        ApplicationConfiguration.Initialize();
        var atlas = new TileAtlas(AssetsDir);   // 所有权交给 MainForm
        Application.Run(new MainForm(atlas));
    }

    private static UiScreen CreateScreen(string name, TileAtlas? atlas) => name switch
    {
        "menu" => new MenuScreen(
            UiLayout.Load(Path.Combine(AssetsDir, "layout", "menu.json")),
            _ => false),
        _ => new TableScreen(atlas),
    };

    private static void RenderToPng(string path, double time, string screenName)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var info = new SKImageInfo((int)UiScreen.Width, (int)UiScreen.Height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        var screen = CreateScreen(screenName, atlas);
        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            screen.Update(time);
            screen.Draw(ctx, time);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered {screenName} -> {path}  (atlas loaded: {atlas.IsLoaded})");
    }

    private static void Bench(int frames)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var info = new SKImageInfo((int)UiScreen.Width, (int)UiScreen.Height);
        using var surface = SKSurface.Create(info);
        var table = new TableScreen(atlas);

        for (int i = 0; i < 10; i++)
        {
            using var warm = new SkiaCanvas(surface.Canvas, atlas);
            table.Draw(warm, i * 0.016);
        }

        var times = new double[frames];
        var total = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            var one = Stopwatch.StartNew();
            using var ctx = new SkiaCanvas(surface.Canvas, atlas);
            table.Draw(ctx, i * 0.016);
            one.Stop();
            times[i] = one.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        Array.Sort(times);
        double avg = total.Elapsed.TotalMilliseconds / frames;
        int idx = Math.Clamp((int)(frames * 0.95) - 1, 0, frames - 1);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "bench: {0} frames, avg {1:F2} ms ({2:F1} fps), p95 {3:F2} ms, atlas={4}",
            frames, avg, 1000.0 / avg, times[idx], atlas.IsLoaded));
    }
}
