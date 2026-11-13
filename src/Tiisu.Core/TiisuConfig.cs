using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tiisu.Core;

/// <summary>
/// 本机可编辑配置（TOML 子集）：<c>%APPDATA%\TiisuMahjong\config.toml</c>。
/// 只放开发/调试开关，游戏规则设置仍在 settings.json。
/// </summary>
public sealed class TiisuConfig
{
    private static readonly Lazy<TiisuConfig> Lazy = new(() => Load(), isThreadSafe: true);

    public static TiisuConfig Current => Lazy.Value;

    public static string PathFor()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiisuMahjong");
        return Path.Combine(dir, "config.toml");
    }

    public bool LogKeys { get; private set; }

    public bool PlayTrace { get; private set; }

    public bool PerfEnabled { get; private set; }

    public string PolicyWeights { get; private set; } = string.Empty;

    public string DevRoot { get; private set; } = string.Empty;

    public static TiisuConfig Load()
    {
        string path = PathFor();
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, DefaultToml, new UTF8Encoding(false));
                return Parse(DefaultToml);
            }

            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            return Parse(DefaultToml);
        }
    }

    /// <summary>解析 TOML 子集：section、key = bool/string/int、# 注释。未识别键忽略。</summary>
    public static TiisuConfig Parse(string text)
    {
        var config = new TiisuConfig();
        string section = string.Empty;
        foreach (string raw in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            string line = StripComment(raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            string full = (section + "." + key).Trim('.').ToLowerInvariant();
            switch (full)
            {
                case "debug.log_keys":
                    config.LogKeys = ToBool(value);
                    break;
                case "debug.play_trace":
                    config.PlayTrace = ToBool(value);
                    break;
                case "perf.enabled":
                    config.PerfEnabled = ToBool(value);
                    break;
                case "ai.policy_weights":
                    config.PolicyWeights = ToText(value);
                    break;
                case "dev.root":
                    config.DevRoot = ToText(value);
                    break;
            }
        }

        return config;
    }

    private static string StripComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inString = !inString;
            }
            else if (line[i] == '#' && !inString)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static bool ToBool(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static string ToText(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }

    public const string DefaultToml = """
        # Tiisu Mahjong 本地开发/调试配置
        # 改完重启游戏生效。游戏规则（可选役等）仍由游戏内设置管理。

        [debug]
        # 记录键盘与屏幕切换日志到 %TEMP%\tiisu-debug.log
        log_keys = false

        # 自走/回放时逐步打印（DevTools 用）
        play_trace = false

        [perf]
        # 实际游戏性能埋点：牌桌 HUD + %TEMP%\tiisu-perf.log
        enabled = false

        [ai]
        # 7Neuu 权重 json 路径；留空 = 用内置
        policy_weights = ""

        [dev]
        # DevTools 定位仓库根目录；留空 = 自动探测
        root = ""
        """;
}
