using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Tiisu.App.Ui;

/// <summary>
/// 轻量 UI 文案表：中文原文为 key，英文表在 Assets/i18n/en-US.json。
/// 只覆盖已迁移的核心导航页面；缺失时回退中文，避免出现空字符串。
/// </summary>
public static class UiText
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal);
    private static string _language = "zh-CN";

    public static string Language => _language;
    public static bool IsEnglish => _language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public static void SetLanguage(string? language)
    {
        _language = Normalize(language);
        English.Clear();
        if (!IsEnglish)
        {
            return;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "i18n", "en-US.json");
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (map is not null)
            {
                foreach (var pair in map)
                {
                    English[pair.Key] = pair.Value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 翻译文件损坏时回退中文，不影响游戏。
        }
    }

    public static string Normalize(string? language) =>
        language is not null && language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";

    public static string Tr(string? chinese)
    {
        if (string.IsNullOrEmpty(chinese) || !IsEnglish)
        {
            return chinese ?? string.Empty;
        }

        if (English.TryGetValue(chinese, out string? translated))
        {
            return translated;
        }

        // 带数字的固定句式：不放进 map 的 key 里。
        if (chinese.StartsWith("第 ", StringComparison.Ordinal) && chinese.EndsWith(" 页", StringComparison.Ordinal))
        {
            return "Page " + chinese[2..^2].Trim();
        }

        if (chinese.StartsWith("本机累计 · ", StringComparison.Ordinal) && chinese.EndsWith(" 个小局", StringComparison.Ordinal))
        {
            return "Local total · " + chinese["本机累计 · ".Length..^" 个小局".Length] + " rounds";
        }

        if (chinese.StartsWith("已解锁 ", StringComparison.Ordinal))
        {
            return "Unlocked " + chinese["已解锁 ".Length..];
        }

        if (chinese.EndsWith(" 秒", StringComparison.Ordinal))
        {
            return chinese[..^2] + " s";
        }

        return chinese;
    }
}
