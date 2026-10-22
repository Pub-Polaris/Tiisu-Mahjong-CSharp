using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    }

    public Settings Clone()
    {
        var copy = new Settings();
        copy.CopyFrom(this);
        return copy;
    }
}
