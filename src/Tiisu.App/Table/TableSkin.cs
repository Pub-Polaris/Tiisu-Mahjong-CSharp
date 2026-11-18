using System;
using Tiisu.Localization;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Table;

/// <summary>
/// 牌桌皮肤的具名区域。美术只交这些区域的图（九宫格或单图/图集切片），代码里每块区域
/// 各自独立回退：缺哪块，哪块继续走原来的 <see cref="ICanvas.FillRoundRect"/> / <see cref="ICanvas.DrawText"/>。
/// </summary>
public enum TableSkinRegion
{
    /// <summary>桌面主体（原 Margin 内缩的 20px 圆角绿桌）。</summary>
    Table,

    /// <summary>中央信息板底板（原 400×400 深灰圆角板 + 描边；文字仍逐帧绘制）。</summary>
    CenterPlate,

    /// <summary>四家牌河底（可选：给牌河加一块凹槽/垫布）。</summary>
    RiverBed,

    /// <summary>按钮类小面板（牌桌内的报听标 / 档位标，抽屉式九宫格）。</summary>
    Button,

    /// <summary>整张画布的外框/压边（画在桌面之上）。</summary>
    Frame,

    /// <summary>桌面下方投影（画在桌面之下）。</summary>
    Shadow,
}

/// <summary>一块皮肤区域：切片 + 目标矩形微调 + 透明度。</summary>
public sealed class SkinRegion
{
    internal SkinRegion(SkinSprite sprite, float inflate, float alpha)
    {
        Sprite = sprite;
        Inflate = inflate;
        Alpha = Math.Clamp(alpha, 0f, 1f);
    }

    public SkinSprite Sprite { get; }

    /// <summary>目标矩形外扩（负值内缩），逻辑像素。</summary>
    public float Inflate { get; }

    /// <summary>整体透明度 0–1。</summary>
    public float Alpha { get; }

    public bool IsValid => Sprite.IsValid && Alpha > 0f;
}

/// <summary>
/// 一张牌桌皮肤（<c>skin.json</c> + PNG/图集）。只描述「静态视觉」，不含布局与文字：
/// 布局仍由 <see cref="TableLayoutSpec"/> / <see cref="YogaTableLayout"/> 决定，本类按布局给出的
/// 矩形绘制，所以美术改皮不会动牌位、命中区与规则。
///
/// <para>所有失败路径都返回「空皮肤」（<see cref="None"/>），绘制继续走程序化路径，不影响开桌。</para>
/// </summary>
public sealed class TableSkin : IDisposable
{
    /// <summary>
    /// 清单版本。新版清单可以在旧运行时不崩：版本高于支持值时整张皮忽略、回退程序化。
    /// </summary>
    public const int SupportedVersion = 1;

    /// <summary>皮肤清单文件名（放 <c>Assets/ui/</c> 下即自动启用）。</summary>
    public const string FileName = "skin.json";

    /// <summary>程序化桌面的外圈底色，也是没皮肤时的默认值。</summary>
    public static readonly Color DefaultBackground = Color.Rgb(20, 30, 24);

    /// <summary>空皮肤：所有区域都回退程序化绘制。全局单例，不要 Dispose。</summary>
    public static readonly TableSkin None = new("-", 0, DefaultBackground, new Dictionary<TableSkinRegion, SkinRegion>());

    private readonly Dictionary<TableSkinRegion, SkinRegion> _regions;
    private readonly List<SkinImage> _images;

    private TableSkin(string cacheKey, long stamp, Color background,
        Dictionary<TableSkinRegion, SkinRegion> regions, List<SkinImage>? images = null)
    {
        CacheKey = cacheKey;
        Stamp = stamp;
        Background = background;
        _regions = regions;
        _images = images ?? new List<SkinImage>();
    }

    /// <summary>皮肤名（清单里的 name，缺省用文件名）。</summary>
    public string Name { get; private init; } = string.Empty;

    /// <summary>作者（清单里的 author；交付/追责用）。</summary>
    public string Author { get; private init; } = string.Empty;

    /// <summary>清单路径；空皮肤为 "-"。</summary>
    public string SourcePath { get; private init; } = "-";

    /// <summary>皮肤版本号（缓存键的一部分，换皮后静态层重建）。</summary>
    public long Stamp { get; }

    /// <summary>静态层缓存键：加载期算好一次，每帧不拼字符串。</summary>
    public string CacheKey { get; }

    /// <summary>桌面外圈底色（清单可覆盖）。</summary>
    public Color Background { get; }

    /// <summary>至少有一块可用区域。false = 与当前视觉完全一致的程序化路径。</summary>
    public bool IsActive => _regions.Count > 0;

    /// <summary>可用区域数量。</summary>
    public int RegionCount => _regions.Count;

    public bool Has(TableSkinRegion region) => _regions.ContainsKey(region);

    /// <summary>取一块区域做绘制；返回 false = 没有资源，调用方继续程序化回退。</summary>
    public bool TryDraw(ICanvas c, TableSkinRegion region, RectF dst, float alpha = 1f)
    {
        if (!_regions.TryGetValue(region, out SkinRegion? skinRegion) || !skinRegion.IsValid)
        {
            return false;
        }

        RectF target = skinRegion.Inflate != 0f ? dst.Inflate(skinRegion.Inflate) : dst;
        return SkinDraw.TryDraw(c, skinRegion.Sprite, target, alpha * skinRegion.Alpha);
    }

    /// <summary>按钮区域（给 Ui 层的 ActionBar 预留：<c>TableSkin.TryDrawButton</c> 一行接入）。</summary>
    public bool TryDrawButton(ICanvas c, RectF dst, float alpha = 1f) => TryDraw(c, TableSkinRegion.Button, dst, alpha);

    /// <summary>区域在逻辑坐标下的建议尺寸（美术交付自检 / 编辑器用）；没有该区域返回 false。</summary>
    public bool TryGetRegionSize(TableSkinRegion region, out float width, out float height)
    {
        if (_regions.TryGetValue(region, out SkinRegion? skinRegion) && skinRegion.IsValid)
        {
            width = skinRegion.Sprite.LogicalWidth;
            height = skinRegion.Sprite.LogicalHeight;
            return true;
        }

        width = 0f;
        height = 0f;
        return false;
    }

    public void Dispose()
    {
        foreach (SkinImage image in _images)
        {
            image.Dispose();
        }

        _images.Clear();
        _regions.Clear();
    }

    // ── 清单解析 ─────────────────────────────────────────────

    /// <summary>
    /// 读一张皮肤清单。任何问题（文件缺失 / JSON 错 / 图缺失 / 版本超前）都返回 null 并给出
    /// <paramref name="error"/>，调用方据此回退空皮肤；**绝不抛异常到牌桌绘制路径**。
    /// </summary>
    public static TableSkin? Load(string jsonPath, out string? error)
    {
        error = null;
        try
        {
            string fullPath = Path.GetFullPath(jsonPath);
            if (!File.Exists(fullPath))
            {
                error = L.T("app.table.skin.noManifest");
                return null;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            };
            string text = File.ReadAllText(fullPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<Manifest>(text, options);
            if (manifest is null)
            {
                error = L.T("app.table.skin.emptyManifest");
                return null;
            }

            if (manifest.Version is int version && version > SupportedVersion)
            {
                error = L.T("app.table.skin.versionTooNew", ("version", version), ("supported", SupportedVersion));
                return null;
            }

            string baseDir = Path.GetDirectoryName(fullPath) ?? ".";
            string? atlas = FirstNonEmpty(manifest.Atlas, manifest.Image);

            var images = new Dictionary<string, SkinImage>(StringComparer.OrdinalIgnoreCase);
            var regions = new Dictionary<TableSkinRegion, SkinRegion>();
            int skipped = 0;
            var declared = manifest.Regions;
            if (declared is not null)
            {
                foreach (var pair in declared)
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    if (!TryParseRegionId(pair.Key, out TableSkinRegion id))
                    {
                        // 未知区域：忽略，保持向前兼容（新版本清单可以多写区域）。
                        skipped++;
                        continue;
                    }

                    var dto = pair.Value;
                    string? imageName = FirstNonEmpty(dto.Image, atlas);
                    if (imageName is null)
                    {
                        skipped++;
                        continue;
                    }

                    SkinImage? image = LoadImage(images, ResolvePath(baseDir, imageName));
                    if (image is null)
                    {
                        skipped++;
                        continue;
                    }

                    var source = dto.SourceRect is { Length: 4 } src
                        ? ToRect(src)
                        : (dto.Rect is { Length: 4 } rect
                            ? ToRect(rect)
                            : new RectF(0, 0, image.Width, image.Height));
                    source = ClampSource(source, image);
                    if (source.W <= 0.01f || source.H <= 0.01f)
                    {
                        skipped++;
                        continue;
                    }

                    var slice = dto.Slice is { Length: 4 } values
                        ? new NineSlice(values[0], values[1], values[2], values[3])
                        : NineSlice.None;
                    float scale = dto.Scale ?? manifest.Scale ?? 1f;
                    if (scale <= 0.01f)
                    {
                        scale = 1f;
                    }

                    float alpha = dto.Alpha ?? manifest.Alpha ?? 1f;
                    float inflate = dto.Inflate ?? 0f;
                    regions[id] = new SkinRegion(new SkinSprite(image, source, slice, scale), inflate, alpha);
                }
            }

            if (regions.Count == 0)
            {
                foreach (SkinImage image in images.Values)
                {
                    image.Dispose();
                }

                error = skipped > 0
                    ? L.T("app.table.skin.regionsUnavailable", ("skipped", skipped))
                    : L.T("app.table.skin.noRegions");
                return null;
            }

            string name = FirstNonEmpty(manifest.Name) ?? Path.GetFileNameWithoutExtension(fullPath);
            Color background = TryParseColor(manifest.Background, out Color color) ? color : DefaultBackground;
            long stamp = File.GetLastWriteTimeUtc(fullPath).Ticks;
            string cacheKey = "skin|" + fullPath + "|" + stamp.ToString(CultureInfo.InvariantCulture)
                              + "|" + regions.Count.ToString(CultureInfo.InvariantCulture);

            return new TableSkin(cacheKey, stamp, background, regions, new List<SkinImage>(images.Values))
            {
                Name = name,
                Author = FirstNonEmpty(manifest.Author) ?? string.Empty,
                SourcePath = fullPath,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>区域 id → 枚举；接受 kebab-case 与 camelCase 两种写法。</summary>
    public static bool TryParseRegionId(string? id, out TableSkinRegion region)
    {
        switch (id?.Trim().ToLowerInvariant())
        {
            case "table":
            case "desk":
                region = TableSkinRegion.Table;
                return true;
            case "center-plate":
            case "centerplate":
            case "center":
            case "plate":
                region = TableSkinRegion.CenterPlate;
                return true;
            case "river-bed":
            case "riverbed":
            case "river":
                region = TableSkinRegion.RiverBed;
                return true;
            case "button":
            case "buttons":
                region = TableSkinRegion.Button;
                return true;
            case "frame":
            case "border":
                region = TableSkinRegion.Frame;
                return true;
            case "shadow":
            case "drop-shadow":
                region = TableSkinRegion.Shadow;
                return true;
            default:
                region = default;
                return false;
        }
    }

    private static SkinImage? LoadImage(Dictionary<string, SkinImage> cache, string path)
    {
        if (cache.TryGetValue(path, out SkinImage? existing))
        {
            return existing;
        }

        SkinImage? image = SkinImage.Load(path);
        if (image is not null)
        {
            cache[path] = image;
        }

        return image;
    }

    /// <summary>支持相对清单目录 / 绝对路径 / 反斜杠（Windows 美工交付）。</summary>
    private static string ResolvePath(string baseDir, string name)
    {
        string normalized = name.Replace('\\', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized) ? normalized : Path.Combine(baseDir, normalized);
    }

    private static RectF ToRect(float[] values) => new(values[0], values[1], values[2], values[3]);

    private static RectF ClampSource(RectF source, SkinImage image)
    {
        float x = Math.Clamp(source.X, 0f, image.Width);
        float y = Math.Clamp(source.Y, 0f, image.Height);
        float w = Math.Clamp(source.W, 0f, image.Width - x);
        float h = Math.Clamp(source.H, 0f, image.Height - y);
        return new RectF(x, y, w, h);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary>#RRGGBB 或 #AARRGGBB；失败返回 false（调用方用默认底色）。</summary>
    private static bool TryParseColor(string? text, out Color color)
    {
        color = DefaultBackground;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8)
            || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }

        if (hex.Length == 6)
        {
            color = new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        else
        {
            color = new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value, (byte)(value >> 24));
        }

        return true;
    }

    private sealed class Manifest
    {
        [JsonPropertyName("version")]
        public int? Version { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("atlas")]
        public string? Atlas { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }

        [JsonPropertyName("scale")]
        public float? Scale { get; set; }

        [JsonPropertyName("alpha")]
        public float? Alpha { get; set; }

        [JsonPropertyName("background")]
        public string? Background { get; set; }

        [JsonPropertyName("regions")]
        public Dictionary<string, RegionDto>? Regions { get; set; }
    }

    private sealed class RegionDto
    {
        [JsonPropertyName("image")]
        public string? Image { get; set; }

        /// <summary>源像素矩形 [x, y, w, h]；缺省 = 整图。</summary>
        [JsonPropertyName("src")]
        public float[]? SourceRect { get; set; }

        /// <summary><see cref="SourceRect"/> 的别名。</summary>
        [JsonPropertyName("rect")]
        public float[]? Rect { get; set; }

        /// <summary>九宫格边距 [左, 上, 右, 下]（源像素）；缺省 = 整图拉伸。</summary>
        [JsonPropertyName("slice")]
        public float[]? Slice { get; set; }

        /// <summary>源像素 / 逻辑像素（2 = 2× 交付图）。</summary>
        [JsonPropertyName("scale")]
        public float? Scale { get; set; }

        [JsonPropertyName("alpha")]
        public float? Alpha { get; set; }

        /// <summary>目标矩形外扩（负值内缩），逻辑像素。</summary>
        [JsonPropertyName("inflate")]
        public float? Inflate { get; set; }
    }
}
