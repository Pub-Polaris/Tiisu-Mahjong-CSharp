using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tiisu.App.Rendering;

namespace Tiisu.App.Runtime;

/// <summary>
/// 运行设置（<c>settings.json</c>），字段与 Web 侧同名同义：
/// thinkSeconds / optionalYaku / showMa / showDebug / showWallViewer / recordTiles。
/// </summary>
public sealed class Settings
{
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

    /// <summary>数据目录：<c>%APPDATA%\TiisuMahjong</c>。</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TiisuMahjong");

    public static string SettingsPath(string? directory = null) =>
        Path.Combine(directory ?? DefaultDirectory, "settings.json");

    /// <summary>人类长考秒数（按钮窗口倒计时，UI 用）。</summary>
    public int ThinkSeconds { get; set; } = 30;

    /// <summary>启用的可选役 id（空 = 全部关闭，与 Web 侧默认一致）。</summary>
    public List<string> OptionalYaku { get; set; } = new();

    public bool ShowMa { get; set; }

    public bool ShowDebug { get; set; }

    public bool ShowWallViewer { get; set; }

    /// <summary>音效开关（默认开）。</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>开启后写牌型日志（ASCII JSONL）。</summary>
    public bool RecordTiles { get; set; }

    /// <summary>四档 AI 权重：简单 / 普通 / 困难 / 修罗。</summary>
    public int[] AiWeights { get; set; } = { 30, 45, 20, 5 };

    /// <summary>按座位锁定 AI 档位：-1 = 跟随随机，0..3 = 固定档。</summary>
    public int[] AiSeatLocks { get; set; } = { -1, -1, -1, -1 };

    /// <summary>修罗档 MCTS（实验性，默认关闭）。关闭时修罗完全等价困难档；旧存档缺字段即关闭。</summary>
    public bool AiMctsEnabled { get; set; }

    /// <summary>
    /// 牌桌观感：Mixed（默认，方案 C）/ Flat / Light25D / Medium25D。只影响绘制，不改布局与命中测试。
    /// 旧 settings.json 没有这个键、或新装第一次写盘，都落到 Mixed；
    /// 想回平面写 <c>"tableRenderStyle": "Flat"</c> 即可（重启生效）。
    /// </summary>
    [JsonPropertyName("tableRenderStyle")]
    public TableRenderStyle TableRender { get; set; } = TableRenderStyle.Flat;

    /// <summary>读 <c>settings.json</c>；文件不存在/损坏时返回默认值（与 Web 侧 <c>/api/settings</c> 同口径）。</summary>
    public static Settings Load(string? directory = null)
    {
        string path = SettingsPath(directory);
        if (!File.Exists(path))
        {
            return new Settings();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8), ReadOptions);
            if (loaded is null)
            {
                return new Settings();
            }

            loaded.OptionalYaku ??= new List<string>();
            loaded.AiWeights = NormalizeWeights(loaded.AiWeights);
            loaded.AiSeatLocks = NormalizeLocks(loaded.AiSeatLocks);
            return loaded;
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save(string? directory = null)
    {
        string path = SettingsPath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOptions), new UTF8Encoding(false));
    }

    /// <summary>把另一份设置的内容复制进来（保留本实例引用，供运行中热更新）。</summary>
    public void CopyFrom(Settings other)
    {
        ThinkSeconds = other.ThinkSeconds;
        OptionalYaku = new List<string>(other.OptionalYaku ?? new List<string>());
        ShowMa = other.ShowMa;
        ShowDebug = other.ShowDebug;
        ShowWallViewer = other.ShowWallViewer;
        RecordTiles = other.RecordTiles;
        SoundEnabled = other.SoundEnabled;
        AiWeights = NormalizeWeights(other.AiWeights);
        AiSeatLocks = NormalizeLocks(other.AiSeatLocks);
        AiMctsEnabled = other.AiMctsEnabled;
        TableRender = other.TableRender;
    }

    public Settings Clone()
    {
        var copy = new Settings();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>旧 settings.json 缺字段 / 字段非法时回退默认权重（30/45/20/5）。</summary>
    private static int[] NormalizeWeights(int[]? weights)
    {
        if (weights is null || weights.Length != 4)
        {
            return new[] { 30, 45, 20, 5 };
        }

        var copy = new int[4];
        int sum = 0;
        for (int i = 0; i < 4; i++)
        {
            if (weights[i] < 0)
            {
                return new[] { 30, 45, 20, 5 };
            }

            copy[i] = weights[i];
            sum += weights[i];
        }

        return sum > 0 ? copy : new[] { 30, 45, 20, 5 };
    }

    /// <summary>旧 settings.json 缺字段 / 字段非法时回退全 -1（跟随随机）。</summary>
    private static int[] NormalizeLocks(int[]? locks)
    {
        var copy = new[] { -1, -1, -1, -1 };
        if (locks is null || locks.Length != 4)
        {
            return copy;
        }

        for (int i = 0; i < 4; i++)
        {
            copy[i] = locks[i] is >= 0 and <= 3 ? locks[i] : -1;
        }

        return copy;
    }
}
