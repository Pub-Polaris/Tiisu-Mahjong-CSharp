// Tiisu 牌桌皮肤自检：验证 TableSkin / SkinStore / StaticLayerCache 的关键语义。
// 不是单元测试（tests/ 归另一组），是美工/排障用的可复跑检查：
//   dotnet run --project art/table/skin-demo/verify -c Debug
// 退出码 0 = 全过；非 0 = 有检查失败（输出会指出哪一条）。
using System;
using System.IO;
using System.Text;
using SkiaSharp;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Table;

namespace Tiisu.SkinCheck;

internal static class Program
{
    private static int _failed;

    private static int Main(string[] args)
    {
        try
        {
            // 缩放对照用：--out <png> [--scale 0.714] —— 按宿主 letterbox 缩放渲染一帧，
            // 供「缓存开 / 缓存关」两次进程出图做逐像素比对（Validate the device-scale blit）。
            if (args.Length >= 2 && args[0] == "--out")
            {
                float renderScale = 1f;
                for (int i = 2; i + 1 < args.Length; i++)
                {
                    if (args[i] == "--scale")
                    {
                        renderScale = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                RenderScaledToPng(args[1], renderScale);
                return 0;
            }

            return Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine("SKIN-CHECK: EXCEPTION " + ex);
            return 2;
        }
    }

    /// <summary>按宿主 letterbox 缩放渲染一帧牌桌（与游戏内 ScreenHost.Draw 的变换一致）。</summary>
    private static void RenderScaledToPng(string path, float scale)
    {
        // 默认用示例皮；如果调用方已经用 TIISU_SKIN 指定（off / 别的皮），尊重它。
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SkinStore.EnvVar)))
        {
            Environment.SetEnvironmentVariable(SkinStore.EnvVar, FindDemoDir());
        }

        SkinStore.Reload();

        using var atlas = new TileAtlas(Path.Combine(AppContext.BaseDirectory, "Assets"));
        var scene = new TableSceneSkia();
        var layout = new TableLayoutSpec();
        YogaTableLayout.Apply(layout);
        var vm = TableDemoModel.Create(1);

        int width = (int)MathF.Round(layout.CanvasW * scale);
        int height = (int)MathF.Round(layout.CanvasH * scale);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        canvas.Save();
        canvas.Translate((width - layout.CanvasW * scale) / 2f, (height - layout.CanvasH * scale) / 2f);
        canvas.Scale(scale);
        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            scene.Draw(ctx, vm, layout);
        }

        canvas.Restore();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered scaled table {width}x{height} scale={scale:F3} -> {path}");
    }

    private static int Run()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string here = AppContext.BaseDirectory;
        string demoDir = FindDemoDir();
        string appAssets = Path.Combine(here, "Assets");

        // 1) 没有皮肤 → 完全回退（视觉与旧版一致由出图像素比对另外验证）
        Environment.SetEnvironmentVariable(SkinStore.EnvVar, "off");
        SkinStore.Reload();
        Check("no skin -> fallback", SkinStore.IsFallback && !SkinStore.Current.IsActive);

        // 2) 示例皮肤能加载：六个具名区域 + 底色 + 区域尺寸
        SkinStore.Reload(); // 让 ResolvePath 重新读环境变量
        Environment.SetEnvironmentVariable(SkinStore.EnvVar, demoDir);
        SkinStore.Reload();
        TableSkin skin = SkinStore.Current;
        Check("demo skin active", skin.IsActive, $"path={skin.SourcePath}");
        Check("6 named regions", skin.RegionCount == 6, $"count={skin.RegionCount} name={skin.Name}");
        foreach (TableSkinRegion region in Enum.GetValues<TableSkinRegion>())
        {
            Check($"region present: {region}", skin.Has(region));
        }

        Check("background parsed", skin.Background.R == 0x0d && skin.Background.G == 0x15 && skin.Background.B == 0x12,
            skin.Background.ToString());
        Check("region logical size: center-plate", skin.TryGetRegionSize(TableSkinRegion.CenterPlate, out float pw, out float ph)
            && Math.Abs(pw - 160f) < 0.5f && Math.Abs(ph - 100f) < 0.5f, $"{pw}x{ph}");

        // 3) 静态层缓存：同布局只重建一次；换画布尺寸才重建
        CheckStaticLayerRebuilds(appAssets);

        // 4) 缺区域回退：只有 table 的清单 → 其它区域 TryDraw 必须返回 false（调用方走程序化）
        string partial = WriteTempSkin(demoDir, "partial", """
            { "version": 1, "scale": 2, "atlas": "skin-atlas.png",
              "regions": { "table": { "src": [0, 0, 512, 512], "slice": [96, 96, 96, 96] } } }
            """);
        TableSkin? partialSkin = TableSkin.Load(partial, out string? partialError);
        Check("partial skin loads", partialSkin is not null, partialError ?? string.Empty);
        if (partialSkin is not null)
        {
            using var surface = SKSurface.Create(new SKImageInfo(1400, 1400));
            using var canvas = new SkiaCanvas(surface.Canvas);
            Check("partial: table draws", partialSkin.TryDraw(canvas, TableSkinRegion.Table, new RectF(16, 16, 1368, 1368)));
            Check("partial: center-plate falls back", !partialSkin.TryDraw(canvas, TableSkinRegion.CenterPlate, new RectF(500, 500, 400, 400)));
            Check("partial: button falls back", !partialSkin.TryDrawButton(canvas, new RectF(0, 0, 66, 24)));
            partialSkin.Dispose();
        }

        // 5) 坏清单 / 缺图 / 版本超前 → 返回 null 并给原因，绝不抛异常
        string missingImage = WriteTempSkin(demoDir, "missing-image", """
            { "version": 1, "regions": { "table": { "image": "nope.png" } } }
            """);
        Check("missing image -> null", TableSkin.Load(missingImage, out string? err1) is null && !string.IsNullOrEmpty(err1), err1 ?? "");

        string badJson = WriteTempSkin(demoDir, "bad-json", "{ this is not json ");
        Check("bad json -> null", TableSkin.Load(badJson, out string? err2) is null && !string.IsNullOrEmpty(err2), err2 ?? "");

        string futureVersion = WriteTempSkin(demoDir, "future", """
            { "version": 99, "regions": { "table": { "image": "skin-atlas.png" } } }
            """);
        Check("future version -> null", TableSkin.Load(futureVersion, out string? err3) is null, err3 ?? "");

        // 6) 环境变量 off 时 SkinStore 忽略随包清单
        Environment.SetEnvironmentVariable(SkinStore.EnvVar, "off");
        SkinStore.Reload();
        Check("TIISU_SKIN=off respected", SkinStore.IsFallback && SkinStore.ResolvePath() is null);

        CleanupTempSkins(demoDir);
        Console.WriteLine(_failed == 0 ? "SKIN-CHECK: ALL PASS" : $"SKIN-CHECK: {_failed} FAILED");
        return _failed == 0 ? 0 : 1;
    }

    private static void CheckStaticLayerRebuilds(string assetsDir)
    {
        TableSceneSkia.ClearStaticLayers();
        using var atlas = new TileAtlas(assetsDir);
        var scene = new TableSceneSkia();
        var vm = TableDemoModel.Create(1);

        var layout = new TableLayoutSpec();
        YogaTableLayout.Apply(layout);

        using var surface = SKSurface.Create(new SKImageInfo(1400, 1400));
        DrawOnce(surface, scene, vm, layout);
        var (hits, misses) = StaticLayerCache<TableStaticKey>.Stats;
        Check("first frame builds once", misses == 1 && hits == 0, $"hits={hits} misses={misses}");

        DrawOnce(surface, scene, vm, layout);
        DrawOnce(surface, scene, vm, layout);
        (hits, misses) = StaticLayerCache<TableStaticKey>.Stats;
        Check("same layout -> no rebuild", misses == 1 && hits == 2, $"hits={hits} misses={misses}");

        // 画布尺寸变化 = 布局变化 → 必须重建
        var resized = new TableLayoutSpec { CanvasW = 1200, CanvasH = 1200 };
        YogaTableLayout.Apply(resized);
        using var surface2 = SKSurface.Create(new SKImageInfo(1200, 1200));
        DrawOnce(surface2, scene, vm, resized);
        (hits, misses) = StaticLayerCache<TableStaticKey>.Stats;
        Check("size change -> rebuild", misses == 2, $"hits={hits} misses={misses}");

        // 换皮：SkinStore 换皮会清空静态层（旧位图引用旧图），下一帧必须重建
        Environment.SetEnvironmentVariable(SkinStore.EnvVar, "off");
        SkinStore.Reload();
        Check("skin reload clears static layers", StaticLayerCache<TableStaticKey>.Count == 0,
            $"count={StaticLayerCache<TableStaticKey>.Count}");

        Environment.SetEnvironmentVariable(SkinStore.EnvVar, FindDemoDir());
        SkinStore.Reload();
        DrawOnce(surface, scene, vm, layout);
        (hits, misses) = StaticLayerCache<TableStaticKey>.Stats;
        Check("skin on -> rebuild from cleared cache", misses == 1 && hits == 0, $"hits={hits} misses={misses}");

        TableSceneSkia.ClearStaticLayers();
    }

    private static void DrawOnce(SKSurface surface, TableSceneSkia scene, TableViewModel vm, TableLayoutSpec layout)
    {
        using var ctx = new SkiaCanvas(surface.Canvas);
        scene.Draw(ctx, vm, layout);
        surface.Canvas.Flush();
    }

    private static string WriteTempSkin(string demoDir, string name, string json)
    {
        // 相对图路径按清单所在目录解析，所以临时清单必须和 skin-atlas.png 放同一层。
        string path = Path.Combine(demoDir, "_verify-" + name + ".json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private static void CleanupTempSkins(string demoDir)
    {
        try
        {
            foreach (string file in Directory.GetFiles(demoDir, "_verify-*.json"))
            {
                File.Delete(file);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响检查结论。
        }
    }

    private static string FindDemoDir()
    {
        // 从当前目录/程序目录往上找 art/table/skin-demo
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, "art", "table", "skin-demo");
                if (File.Exists(Path.Combine(candidate, "skin.json")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException("找不到 art/table/skin-demo/skin.json");
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
        {
            Console.WriteLine($"  ok   {name}");
            return;
        }

        _failed++;
        Console.WriteLine($"  FAIL {name}  {detail}");
    }
}
