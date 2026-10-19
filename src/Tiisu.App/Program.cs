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

    private static void RenderTableToPng(string path, bool debug = false)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var info = new SKImageInfo((int)TableLayout.Width, (int)TableLayout.Height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        var vm = SampleTableModel();
        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            new TableScene().Draw(ctx, vm, 0.6, debug);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine("rendered table -> " + path);
    }

    private static TableViewModel SampleTableModel()
    {
        TileView T(string id) => new(id);
        MeldView M(string type, params string[] ids) => new() { Type = type, Tiles = ids.Select(T).ToArray() };

        // 1 副露：手牌 10 + 摸牌 1 = 11
        var hand0 = new[] { "1m", "2m", "3m", "5m", "5m", "7m", "2p", "3p", "4p", "1s", "3z" }
            .Select(T).ToArray();
        var river0 = new[] { "3p", "8p", "1z", "9s", "2p", "5s", "6z", "7m", "1p", "4s", "2z", "4m" }.Select(T).ToArray();
        var river1 = new[] { "2m", "9m", "6p", "3z", "7s", "5z", "1m", "4z", "6s" }.Select(T).ToArray();
        var river2 = new[] { "1p", "2p", "3p", "4p", "5p", "6p", "7p", "8p", "9p", "1z", "2z", "3z", "4z", "5z", "6z" }.Select(T).ToArray();
        var river3 = new[] { "9m", "8m", "7m", "6m", "5m", "4m", "3m" }.Select(T).ToArray();

        var seats = new List<SeatView>
        {
            new() { Index = 0, Name = "你", Wind = "东", Score = 49000, IsDealer = true, IsHuman = true, IsTurn = true,
                    HandCount = 11, Hand = hand0, Discards = river0,
                    Melds = new[] { M("pon", "2s", "2s", "2s") } },
            new() { Index = 1, Name = "南 AI", Wind = "南", Score = 39200, HandCount = 10, Discards = river1,
                    Melds = new[] { M("chi", "4m", "5m", "6m") } },
            new() { Index = 2, Name = "西 AI", Wind = "西", Score = 58800, HandCount = 7, Discards = river2,
                    Melds = new[] { M("pon", "7z", "7z", "7z"), M("pon", "1z", "1z", "1z") } },
            new() { Index = 3, Name = "北 AI", Wind = "北", Score = 49000, HandCount = 10, Discards = river3,
                    Melds = new[] { M("chi", "7s", "8s", "9s") } },
        };

        return new TableViewModel
        {
            RoundLabel = "东 1 局 · 0 本场",
            WallCount = 36,
            RinshanCount = 4,
            Honba = 0,
            Kyotaku = 0,
            TurnIndex = 0,
            Seats = seats,
        };
    }

    private static void ExportSvg(string outDir)
    {
        Directory.CreateDirectory(outDir);
        string tilesDir = Path.Combine(FindWebDir(), "mj_tiles");
        var exporter = new SvgExporter(tilesDir);
        var vm = SampleTableModel();

        string main = Path.Combine(outDir, "table-generated.svg");
        string regions = Path.Combine(outDir, "table-regions.svg");
        File.WriteAllText(main, exporter.Build(vm), new System.Text.UTF8Encoding(false));
        File.WriteAllText(regions, SvgExporter.BuildRegions(), new System.Text.UTF8Encoding(false));
        string layoutJson = Path.Combine(outDir, "layout.json");
        string layoutMd = Path.Combine(outDir, "layout.md");
        File.WriteAllText(layoutJson, SvgExporter.BuildLayoutJson(), new System.Text.UTF8Encoding(false));
        File.WriteAllText(layoutMd, SvgExporter.BuildLayoutMarkdown(), new System.Text.UTF8Encoding(false));
        Console.WriteLine($"exported svg -> {main}（内嵌牌面 {exporter.TileCount} 张）/ {regions} / layout.json / layout.md");
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
        const int legendH = 150;
        var info = new SKImageInfo((int)TableLayout.Width, (int)TableLayout.Height + legendH);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(18, 26, 22));

        var vm = SampleTableModel();
        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            new TableScene().Draw(ctx, vm, 0.6, debug: false);
        }

        DrawRegionNumbers(canvas);
        DrawLegend(canvas, (int)TableLayout.Height, legendH);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine("rendered annotated -> " + path);
    }

    /// <summary>在每个区域左上角画大号编号。</summary>
    private static void DrawRegionNumbers(SKCanvas canvas)
    {
        var regions = new (string Num, string Name, RectF R)[]
        {
            ("1", "自家手牌", TableLayout.HandArea),
            ("2", "自家副露", TableLayout.OwnMeldArea),
            ("3", "对家手牌", TableLayout.TopHandArea),
            ("4", "对家副露", TableLayout.TopMeldArea),
            ("5", "上家手牌", TableLayout.LeftHandArea),
            ("6", "上家副露", TableLayout.LeftMeldArea),
            ("7", "下家手牌", TableLayout.RightHandArea),
            ("8", "下家副露", TableLayout.RightMeldArea),
            ("9", "中央板", TableLayout.CenterPlate),
            ("A", "自家牌河", TableLayout.River(0).Bounds),
            ("B", "对家牌河", TableLayout.River(2).Bounds),
            ("C", "下家牌河", TableLayout.River(1).Bounds),
            ("D", "上家牌河", TableLayout.River(3).Bounds),
        };

        using var box = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = new SKColor(255, 72, 72) };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(200, 32, 32) };
        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 20f);
        using var text = new SKPaint { IsAntialias = true, Color = SKColors.White };

        foreach (var (num, _, r) in regions)
        {
            canvas.DrawRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), box);
            var badge = new SKRect(r.X, r.Y, r.X + 26, r.Y + 26);
            canvas.DrawRect(badge, fill);
            canvas.DrawText(num, r.X + 7, r.Y + 19, SKTextAlign.Left, font, text);
        }
    }

    /// <summary>底部图例：编号 -> 名称，用口语说明怎么改。</summary>
    private static void DrawLegend(SKCanvas canvas, int top, int height)
    {
        using var bg = new SKPaint { IsAntialias = true, Color = new SKColor(12, 18, 15) };
        canvas.DrawRect(new SKRect(0, top, TableLayout.Width, top + height), bg);

        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 16f);
        using var head = new SKPaint { IsAntialias = true, Color = new SKColor(255, 214, 120) };
        using var body = new SKPaint { IsAntialias = true, Color = new SKColor(228, 236, 228) };

        canvas.DrawText("图例：1 自家手牌  2 自家副露  3 对家手牌  4 对家副露  5 上家手牌  6 上家副露",
            16, top + 24, SKTextAlign.Left, font, body);
        canvas.DrawText("7 下家手牌  8 下家副露  9 中央板  A 自家牌河  B 对家牌河  C 下家牌河  D 上家牌河",
            16, top + 50, SKTextAlign.Left, font, body);
        canvas.DrawText("直接说哪块不对、该在哪就行，比如「4 应该在 3 的右边、和 3 贴住」「A 太靠下，往上挪」",
            16, top + 82, SKTextAlign.Left, font, head);
        canvas.DrawText("（改完我出图，你再指；不需要你填表或改文件）",
            16, top + 108, SKTextAlign.Left, font, body);
    }
}
