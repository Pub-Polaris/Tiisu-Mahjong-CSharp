using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tiisu.App.Rendering;
using Tiisu.App.Table;

namespace Tiisu.App.Runtime;

/// <summary>
/// 牌桌自定义布局（<c>%APPDATA%\TiisuMahjong\layout-custom.json</c>）。
/// 只存区域名 → x/y/w/h；启动时若文件存在就覆盖 <see cref="TableLayoutSpec"/> 默认值。
/// 解析失败 / 字段缺失一律忽略并继续用默认，不抛异常。
/// </summary>
public static class CustomLayoutStore
{
    public const int CurrentVersion = 1;

    public const string FileName = "layout-custom.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string PathFor(string? directory = null) =>
        Path.Combine(directory ?? Settings.DefaultDirectory, FileName);

    public static bool Exists(string? directory = null) => File.Exists(PathFor(directory));

    /// <summary>
    /// 读取并写回布局。返回 true 表示至少覆盖了一个区域。
    /// 文件不存在、JSON 非法、版本不符、区域缺失都返回 false（当前矩形原样保留）。
    /// </summary>
    public static bool Load(TableLayoutSpec layout, string? directory = null)
    {
        string path = PathFor(directory);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            var file = JsonSerializer.Deserialize<LayoutFile>(text, ReadOptions);
            if (file is null)
            {
                Warn(path, "空文件");
                return false;
            }

            if (file.Version is int version && version != CurrentVersion)
            {
                Warn(path, $"版本 {version} 不受支持");
                return false;
            }

            if (file.Regions is null || file.Regions.Count == 0)
            {
                Warn(path, "没有 regions");
                return false;
            }

            var byId = new Dictionary<string, TableLayoutSpec.LayoutRegion>(StringComparer.Ordinal);
            foreach (var region in layout.Regions)
            {
                byId[region.Id] = region;
            }

            int applied = 0;
            foreach (var (id, dto) in file.Regions)
            {
                if (!byId.TryGetValue(id, out var region)
                    || dto is null
                    || dto.X is not float x || dto.Y is not float y
                    || dto.W is not float w || dto.H is not float h
                    || !IsFinite(x) || !IsFinite(y) || !IsFinite(w) || !IsFinite(h)
                    || w <= 0f || h <= 0f)
                {
                    continue;
                }

                region.Set(new RectF(x, y, w, h));
                applied++;
            }

            if (applied > 0)
            {
                layout.OverridesActive = true;
            }

            return applied > 0;
        }
        catch (Exception ex)
        {
            Warn(path, ex.Message);
            return false;
        }
    }

    /// <summary>写全部区域（含默认值），版本号 + 各区域 x/y/w/h。</summary>
    public static void Save(TableLayoutSpec layout, string? directory = null)
    {
        string path = PathFor(directory);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var regions = new Dictionary<string, RegionDto>(StringComparer.Ordinal);
        foreach (var region in layout.Regions)
        {
            var r = region.Rect;
            if (r.W <= 0f || r.H <= 0f)
            {
                // 结算面板高度 0 = 自适应，写入时给一个可读的默认高。
                continue;
            }

            regions[region.Id] = new RegionDto { X = r.X, Y = r.Y, W = r.W, H = r.H };
        }

        var file = new LayoutFile { Version = CurrentVersion, Regions = regions };
        File.WriteAllText(path, JsonSerializer.Serialize(file, WriteOptions), new UTF8Encoding(false));
    }

    /// <summary>删除自定义文件（恢复默认布局）。</summary>
    public static bool Delete(string? directory = null)
    {
        string path = PathFor(directory);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex)
        {
            Warn(path, ex.Message);
        }

        return false;
    }

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    private static void Warn(string path, string message)
    {
        // 自检/控制台可读；GUI 下 Console 不存在时静默。
        string line = $"[layout] 忽略自定义布局 {path}: {message}";
        Debug.WriteLine(line);
        try
        {
            Console.Error.WriteLine(line);
        }
        catch (IOException)
        {
            // 无控制台：忽略。
        }
    }

    private sealed class LayoutFile
    {
        [JsonPropertyName("version")]
        public int? Version { get; set; }

        [JsonPropertyName("regions")]
        public Dictionary<string, RegionDto>? Regions { get; set; }
    }

    private sealed class RegionDto
    {
        [JsonPropertyName("x")]
        public float? X { get; set; }

        [JsonPropertyName("y")]
        public float? Y { get; set; }

        [JsonPropertyName("w")]
        public float? W { get; set; }

        [JsonPropertyName("h")]
        public float? H { get; set; }
    }
}
