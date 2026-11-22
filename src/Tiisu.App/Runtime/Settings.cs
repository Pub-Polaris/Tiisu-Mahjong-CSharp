using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tiisu.App.Rendering;
using Tiisu.Core;

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
        // 与 Web 的 camelCase 对齐（P1-3）：否则 Web 读不到 showMa / optionalYaku 等字段。
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>数据目录：<c>%APPDATA%\TiisuMahjong</c>（旧平铺目录，迁移/共享缓存用）。</summary>
    public static string DefaultDirectory => AppData.Root;

    /// <summary>本设置对象所属的数据目录；<see cref="Save()"/> 写这里。旧代码 new Settings() 落根目录。</summary>
    [JsonIgnore]
    public string DataDirectory { get; set; } = AppData.Root;

    public static string SettingsPath(string? directory = null) =>
        Path.Combine(directory ?? AppData.Root, "settings.json");

    /// <summary>规则集：Qizi（默认，七子）或 TuidaoHu（推倒胡，1102工程）。</summary>
    [JsonPropertyName("ruleset")]
    public Ruleset Ruleset { get; set; } = Ruleset.Qizi;

    /// <summary>人类长考秒数（按钮窗口倒计时，UI 用）。</summary>
    public int ThinkSeconds { get; set; } = 30;

    /// <summary>背景音乐音量 0..100（0 = 静音）。</summary>
    [JsonPropertyName("bgmVolume")]
    public int BgmVolume { get; set; } = 60;

    /// <summary>音效音量 0..100（0 = 静音）。</summary>
    [JsonPropertyName("sfxVolume")]
    public int SfxVolume { get; set; } = 80;

    /// <summary>启用的可选役 id（空 = 全部关闭，与 Web 侧默认一致）。</summary>
    public List<string> OptionalYaku { get; set; } = new();

    public bool ShowMa { get; set; }

    public bool ShowDebug { get; set; }

    public bool ShowWallViewer { get; set; }

    /// <summary>音效开关（默认开）。</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>报牌语音开关（默认开；与音效独立）。</summary>
    public bool VoiceEnabled { get; set; } = true;

    /// <summary>界面字号倍率：1.0 / 1.15 / 1.3（仅非牌桌页面）。</summary>
    public double UiTextScale { get; set; } = 1.0;

    /// <summary>联机玩家名；档案是权威，这里保留为兼容镜像。</summary>
    public string PlayerName { get; set; } = "";

    public string EffectivePlayerName =>
        string.IsNullOrWhiteSpace(PlayerName) ? Environment.UserName : PlayerName;

    /// <summary>UI 语言：zh-CN / en-US / ja-JP。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>开启后写牌型日志（ASCII JSONL）。默认开：牌谱复盘依赖它；旧 settings.json 显式 false 时保持关。</summary>
    public bool RecordTiles { get; set; } = true;

    /// <summary>四档 AI 权重：简单 / 普通 / 困难 / 修罗。</summary>
    public int[] AiWeights { get; set; } = { 30, 45, 20, 5 };

    /// <summary>按座位锁定 AI 档位：-1 = 跟随随机，0..3 = 固定档。</summary>
    public int[] AiSeatLocks { get; set; } = { -1, -1, -1, -1 };

    /// <summary>修罗档 MCTS（实验性，默认关闭）。关闭时修罗完全等价困难档；旧存档缺字段即关闭。</summary>
    public bool AiMctsEnabled { get; set; }

    /// <summary>
    /// 7Neuu 自动追役原型开关（默认关闭）。关闭时 7Neuu 决策与旧版逐位一致；
    /// 打开后只有 7Neuu 会跑追役判定，其他档位不受影响。旧 settings.json 缺字段即关闭。
    /// </summary>
    [JsonPropertyName("aiPursuitEnabled")]
    public bool AiPursuitEnabled { get; set; }

    /// <summary>
    /// 五项 AI 风格权重（进攻 / 防守 / 副露 / 打点 / 追役，0..2，默认全 1.0）。
    /// 只在 7Neuu 追役原型里生效；旧 settings.json 缺字段时落默认中性风格。
    /// </summary>
    [JsonPropertyName("aiStyle")]
    public Tiisu.Engine.Ai.AiStyle AiStyle { get; set; } = new();

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
        string dataDirectory = AppData.Resolve(directory);
        string path = SettingsPath(dataDirectory);
        if (!File.Exists(path))
        {
            return new Settings { DataDirectory = dataDirectory };
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8), ReadOptions);
            if (loaded is null)
            {
                return new Settings { DataDirectory = dataDirectory };
            }

            loaded.DataDirectory = dataDirectory;
            loaded.OptionalYaku ??= new List<string>();
            loaded.AiWeights = NormalizeWeights(loaded.AiWeights);
            loaded.AiSeatLocks = NormalizeLocks(loaded.AiSeatLocks);
            loaded.AiStyle = (loaded.AiStyle ?? Tiisu.Engine.Ai.AiStyle.Default).Clamped();
            loaded.UiTextScale = Math.Clamp(loaded.UiTextScale, 1.0, 1.3);
            loaded.Language = Tiisu.App.Ui.UiText.Normalize(loaded.Language);
            loaded.PlayerName = NormalizePlayerName(loaded.PlayerName);
            return loaded;
        }
        catch
        {
            // 坏文件改名留证（P1-4）：至少让人知道数据被丢过。
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch
            {
                // 改名失败也不阻断启动
            }

            return new Settings { DataDirectory = dataDirectory };
        }
    }

    public void Save(string? directory = null)
    {
        string dataDirectory = AppData.Resolve(directory ?? DataDirectory);
        string path = SettingsPath(dataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 原子写：先 .tmp 再替换，避免崩溃留下半截设置（P1-4）。
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, WriteOptions), new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Replace(temp, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }

        DataDirectory = dataDirectory;
    }

    /// <summary>把另一份设置的内容复制进来（保留本实例引用，供运行中热更新）。</summary>
    public void CopyFrom(Settings other)
    {
        ThinkSeconds = other.ThinkSeconds;
        OptionalYaku = new List<string>(other.OptionalYaku ?? new List<string>());
        ShowMa = other.ShowMa;
        Ruleset = other.Ruleset;
        BgmVolume = other.BgmVolume;
        SfxVolume = other.SfxVolume;
        ShowDebug = other.ShowDebug;
        ShowWallViewer = other.ShowWallViewer;
        RecordTiles = other.RecordTiles;
        SoundEnabled = other.SoundEnabled;
        VoiceEnabled = other.VoiceEnabled;
        UiTextScale = other.UiTextScale;
        Language = other.Language;
        AiWeights = NormalizeWeights(other.AiWeights);
        AiSeatLocks = NormalizeLocks(other.AiSeatLocks);
        AiMctsEnabled = other.AiMctsEnabled;
        AiPursuitEnabled = other.AiPursuitEnabled;
        AiStyle = (other.AiStyle ?? Tiisu.Engine.Ai.AiStyle.Default).Clamped();
        TableRender = other.TableRender;
        PlayerName = NormalizePlayerName(other.PlayerName);
    }

    public Settings Clone()
    {
        var copy = new Settings { DataDirectory = DataDirectory };
        copy.CopyFrom(this);
        return copy;
    }

    public static string NormalizePlayerName(string? name)
    {
        string value = (name ?? string.Empty).Trim();
        return value.Length <= 24 ? value : value[..24];
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
            copy[i] = locks[i] is >= 0 and <= 4 ? locks[i] : -1;
        }

        return copy;
    }
}
