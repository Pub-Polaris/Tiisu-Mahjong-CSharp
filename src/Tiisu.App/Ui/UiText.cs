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

        if (chinese.StartsWith("东", StringComparison.Ordinal)
            || chinese.StartsWith("南", StringComparison.Ordinal)
            || chinese.StartsWith("西", StringComparison.Ordinal)
            || chinese.StartsWith("北", StringComparison.Ordinal))
        {
            string round = chinese
                .Replace("东", "East", StringComparison.Ordinal)
                .Replace("南", "South", StringComparison.Ordinal)
                .Replace("西", "West", StringComparison.Ordinal)
                .Replace("北", "North", StringComparison.Ordinal)
                .Replace("局", "round", StringComparison.Ordinal)
                .Replace("本场", "honba", StringComparison.Ordinal)
                .Replace("场赏", "bonus", StringComparison.Ordinal);
            round = System.Text.RegularExpressions.Regex.Replace(round, @"(?<=[A-Za-z])(?=\d)", " ");
            round = System.Text.RegularExpressions.Regex.Replace(round, @"(?<=\d)(?=[A-Za-z])", " ");
            round = System.Text.RegularExpressions.Regex.Replace(round, @"\s+", " ");
            if (round.Contains("round", StringComparison.Ordinal))
            {
                return round;
            }
        }

        return chinese;
    }
}
