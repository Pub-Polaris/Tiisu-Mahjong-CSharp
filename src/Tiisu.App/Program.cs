using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SkiaSharp;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Ui;
using Tiisu.App.Table;
using Tiisu.App.Svg;
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

        // 带编号的布局图（看图说话用）：--render-annotated <png>
        if (args.Length >= 2 && args[0] == "--render-annotated")
        {
            RenderAnnotated(args[1]);
            return;
        }

        // 导出可编辑 SVG：--export-svg <dir>
        if (args.Length >= 2 && args[0] == "--export-svg")
        {
            ExportSvg(args[1]);
            return;
        }

        // Skia 牌桌渲染（布局验证）：--render-table <png>
        if (args.Length >= 2 && args[0] == "--render-table")
        {
            RenderTableToPng(args[1], args.Length >= 3 && args[2] == "debug");
            return;
        }

        // 界面截图（与游戏内同一条 Screen 路径）：--render-screen <png> [table|menu]
        if (args.Length >= 2 && args[0] == "--render-screen")
        {
            RenderScreenToPng(args[1], args.Length >= 3 ? args[2] : "table");
            return;
        }

        // Yoga 布局预览：--render-yoga <png> [画布边长] [副露组数 0..4]
        if (args.Length >= 2 && args[0] == "--render-yoga")
        {
            float canvasY = args.Length >= 3 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 1200f;
            int meldsY = args.Length >= 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1;
            RenderYogaToPng(args[1], canvasY, meldsY);
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

    /// <summary>按屏自己的逻辑画布尺寸出图（与游戏内走同一条 Screen 路径）。</summary>
    private static void RenderScreenToPng(string path, string screenName)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var screen = CreateScreen(screenName, atlas);

        var info = new SKImageInfo((int)screen.CanvasWidth, (int)screen.CanvasHeight);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            screen.Update(0.6);
            screen.Draw(ctx, 0.6);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered screen '{screenName}' -> {path}  " +
                          $"{screen.CanvasWidth:F0}x{screen.CanvasHeight:F0} (atlas: {atlas.IsLoaded})");
    }

    private static void Bench(int frames)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var table = new TableScreen(atlas);
        var info = new SKImageInfo((int)table.CanvasWidth, (int)table.CanvasHeight);
        using var surface = SKSurface.Create(info);

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

    private static void RenderTableToPng(string path, bool debug = false)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var layout = NewLayout();

        var info = new SKImageInfo((int)layout.CanvasW, (int)layout.CanvasH);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            new TableSceneSkia().Draw(ctx, TableDemoModel.Create(1), layout);
            if (debug)
            {
                DrawDebugRegions(ctx, layout);
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine("rendered table -> " + path);
    }

    /// <summary>1200×1200 的新布局（Yoga 计算）。</summary>
    private static TableLayoutSpec NewLayout()
    {
        var layout = new TableLayoutSpec { CanvasW = 1200f, CanvasH = 1200f };
        YogaTableLayout.Apply(layout);
        return layout;
    }

    private static void DrawDebugRegions(ICanvas c, TableLayoutSpec l)
    {
        var red = Tiisu.App.Rendering.Color.Rgb(255, 72, 72);
        foreach (var (id, note, r) in SvgExporter.Regions(l))
        {
            c.StrokeRoundRect(r, 2, red, 1.5f);
            c.DrawText($"{id} · {note}",
                new Tiisu.App.Rendering.RectF(r.X + 4, r.Y + 2, 280, 16), red,
                new Tiisu.App.Rendering.TextStyle(11, Bold: true, Center: false));
        }
    }

    private static void RenderYogaToPng(string path, float canvas, int selfMelds)
    {
        using var atlas = new TileAtlas(AssetsDir);

        var layout = new TableLayoutSpec { CanvasW = canvas, CanvasH = canvas };
        YogaTableLayout.Apply(layout);

        var info = new SKImageInfo((int)layout.CanvasW, (int)layout.CanvasH);
        using var surface = SKSurface.Create(info);
        var canvasSk = surface.Canvas;
        canvasSk.Clear(SKColors.Black);

        var vm = TableDemoModel.Create(selfMelds);
        using (var ctx = new SkiaCanvas(canvasSk, atlas))
        {
            new TableSceneSkia().Draw(ctx, vm, layout);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());

        Console.WriteLine($"rendered yoga -> {path}  canvas={canvas:F0}");
        Console.WriteLine($"  plate = {layout.PlateRect.X:F1},{layout.PlateRect.Y:F1} {layout.PlateRect.W:F0}x{layout.PlateRect.H:F0}");
        Console.WriteLine($"  self  = {layout.HandRects[0].X:F1},{layout.HandRects[0].Y:F1} {layout.HandRects[0].W:F0}x{layout.HandRects[0].H:F0}");
    }

    private static void ExportSvg(string outDir)
    {
        Directory.CreateDirectory(outDir);
        string tilesDir = Path.Combine(FindWebDir(), "mj_tiles");

        var layout = NewLayout();
        var vm = TableDemoModel.Create(1);
        var exporter = new SvgExporter(tilesDir, layout);

        string main = Path.Combine(outDir, "table-generated.svg");
        File.WriteAllText(main, exporter.Build(vm), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "table-regions.svg"),
            SvgExporter.BuildRegions(layout), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "layout.json"),
            SvgExporter.BuildLayoutJson(layout), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "layout.md"),
            SvgExporter.BuildLayoutMarkdown(layout), new System.Text.UTF8Encoding(false));

        Console.WriteLine($"exported svg -> {main}（内嵌牌面 {exporter.TileCount} 张）/ table-regions.svg / layout.json / layout.md");
    }

    /// <summary>从 exe 目录向上找仓库里的 Web 目录（mj_tiles 来源）。</summary>
    private static string FindWebDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "Web", "mj_tiles");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "Web");
            }

            dir = dir.Parent;
        }

        return "Web";
    }

    private static void RenderAnnotated(string path)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var layout = NewLayout();
        var regions = SvgExporter.Regions(layout).ToArray();

        const int legendH = 120;
        var info = new SKImageInfo((int)layout.CanvasW, (int)(layout.CanvasH + legendH));
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(18, 26, 22));

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            new TableSceneSkia().Draw(ctx, TableDemoModel.Create(1), layout);
        }

        DrawRegionNumbers(canvas, regions);
        DrawLegend(canvas, (int)layout.CanvasW, (int)layout.CanvasH, legendH, regions);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine("rendered annotated -> " + path);
    }

    /// <summary>在每个区域左上角画大号编号。</summary>
    private static void DrawRegionNumbers(SKCanvas canvas, (string Id, string Note, Tiisu.App.Rendering.RectF Rect)[] regions)
    {
        using var box = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = new SKColor(255, 72, 72) };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(200, 32, 32) };
        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 20f);
        using var text = new SKPaint { IsAntialias = true, Color = SKColors.White };

        for (int i = 0; i < regions.Length; i++)
        {
            var r = regions[i].Rect;
            canvas.DrawRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), box);
            var badge = new SKRect(r.X, r.Y, r.X + 30, r.Y + 30);
            canvas.DrawRect(badge, fill);
            canvas.DrawText((i + 1).ToString(CultureInfo.InvariantCulture), r.X + 8, r.Y + 22,
                SKTextAlign.Left, font, text);
        }
    }

    /// <summary>底部图例：编号 -> 区域名。</summary>
    private static void DrawLegend(SKCanvas canvas, int width, int top, int height,
        (string Id, string Note, Tiisu.App.Rendering.RectF Rect)[] regions)
    {
        using var bg = new SKPaint { IsAntialias = true, Color = new SKColor(12, 18, 15) };
        canvas.DrawRect(new SKRect(0, top, width, top + height), bg);

        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 16f);
        using var head = new SKPaint { IsAntialias = true, Color = new SKColor(255, 214, 120) };
        using var body = new SKPaint { IsAntialias = true, Color = new SKColor(228, 236, 228) };

        var line = string.Join("   ",
            regions.Select((r, i) => $"{i + 1} {r.Note}"));
        canvas.DrawText(line, 16, top + 26, SKTextAlign.Left, font, body);
        canvas.DrawText("直接说编号和怎么改就行，比如「3 应该在 2 的右边、和 2 贴住」「6 太靠下，往上挪」",
            16, top + 58, SKTextAlign.Left, font, head);
        canvas.DrawText("（改完我出图，你再指；不需要你填表或改文件）",
            16, top + 88, SKTextAlign.Left, font, body);
    }
}