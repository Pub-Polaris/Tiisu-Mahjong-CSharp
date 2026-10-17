using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tiisu.Core;

/// <summary>ike.json 中的一条役定义（普通役与高役共用）。</summary>
public sealed class YakuDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fu")]
    public int Fu { get; set; }

    [JsonPropertyName("optional")]
    public bool Optional { get; set; }

    [JsonPropertyName("menzen")]
    public bool Menzen { get; set; }

    [JsonPropertyName("kaishoku")]
    public bool Kaishoku { get; set; }

    [JsonPropertyName("fixedPoints")]
    public int? FixedPoints { get; set; }

    [JsonPropertyName("fixedPointsWhenCalled")]
    public bool FixedPointsWhenCalled { get; set; }

    [JsonPropertyName("doubleYakuman")]
    public bool DoubleYakuman { get; set; }

    [JsonPropertyName("selfDrawDouble")]
    public bool SelfDrawDouble { get; set; }

    [JsonPropertyName("pointsBonus")]
    public int? PointsBonus { get; set; }

    [JsonPropertyName("notIf")]
    public List<string>? NotIf { get; set; }

    [JsonPropertyName("tokurei")]
    public List<TokureiDefinition>? Tokurei { get; set; }

    [JsonPropertyName("special")]
    public string? Special { get; set; }
}

/// <summary>特例加计（tokurei）：when 条件命中时加 bonus 点，不并入赋数。</summary>
public sealed class TokureiDefinition
{
    [JsonPropertyName("when")]
    public string When { get; set; } = string.Empty;

    [JsonPropertyName("bonus")]
    public int Bonus { get; set; }
}

/// <summary>
/// 役种数据。默认从内嵌的 ike.json 读取（与 Web 侧同一份数据），也可从任意 JSON 解析。
/// </summary>
public sealed class YakuData
{
    public int BaseFu { get; init; } = 7;

    public int Kachi7SelfBonus { get; init; } = 1;

    public IReadOnlyList<YakuDefinition> Yaku { get; init; } = Array.Empty<YakuDefinition>();

    public IReadOnlyList<YakuDefinition> HighYaku { get; init; } = Array.Empty<YakuDefinition>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static YakuData LoadDefault()
    {
        var assembly = typeof(YakuData).Assembly;
        const string resourceName = "Tiisu.Core.ike.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static YakuData Parse(string json)
    {
        var file = JsonSerializer.Deserialize<YakuFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("ike.json 解析结果为空");
        return new YakuData
        {
            BaseFu = file.BaseFu ?? 7,
            Kachi7SelfBonus = file.Bonus?.Kachi7Self ?? 1,
            Yaku = file.Yaku ?? new List<YakuDefinition>(),
            HighYaku = file.HighYaku ?? new List<YakuDefinition>(),
        };
    }

    /// <summary>JS 的 getYakuFu：先普通役、后高役，找不到返回 0。</summary>
    public int GetYakuFu(string id)
    {
        foreach (var yaku in Yaku)
        {
            if (yaku.Id == id)
            {
                return yaku.Fu;
            }
        }

        foreach (var yaku in HighYaku)
        {
            if (yaku.Id == id)
            {
                return yaku.Fu;
            }
        }

        return 0;
    }

    private sealed class YakuFile
    {
        [JsonPropertyName("baseFu")]
        public int? BaseFu { get; set; }

        [JsonPropertyName("bonus")]
        public BonusFile? Bonus { get; set; }

        [JsonPropertyName("yaku")]
        public List<YakuDefinition>? Yaku { get; set; }

        [JsonPropertyName("highYaku")]
        public List<YakuDefinition>? HighYaku { get; set; }
    }

    private sealed class BonusFile
    {
        [JsonPropertyName("kachi7_self")]
        public int? Kachi7Self { get; set; }
    }
}
