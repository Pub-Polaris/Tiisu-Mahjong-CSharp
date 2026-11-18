using System;
using System.Collections.Generic;
using Tiisu.Localization;

namespace Tiisu.App.Ui;

/// <summary>
/// 轻量 UI 文案表：中文原文为 key，日文/英文表在 Assets/i18n/{lang}.json。
/// 缺失时回退中文，避免出现空字符串。
/// </summary>
public static class UiText
{

    public static string Language => L.Language;
    public static event Action? LanguageChanged;
    public static bool IsEnglish => L.Language == "en-US";
    public static bool IsJapanese => L.Language == "ja-JP";

    public static void SetLanguage(string? language)
    {
        string before = L.Language;
        L.SetLanguage(language);
        if (!string.Equals(before, L.Language, StringComparison.Ordinal))
        {
            LanguageChanged?.Invoke();
        }
    }

    public static string Normalize(string? language) => L.Normalize(language);

    public static string Tr(string? chinese)
    {
        if (string.IsNullOrEmpty(chinese))
        {
            return string.Empty;
        }

        if (L.HasKey(chinese))
        {
            return L.T(chinese);
        }

        if (L.Language == "zh-CN")
        {
            return chinese;
        }

        if (IsJapanese)
        {
            return TrJapanesePatterns(chinese);
        }

        // 兼容旧的中文 key 动态提示；迁移完成后这些分支会成为兜底。
        if (chinese.StartsWith("未知命令：", StringComparison.Ordinal))
        {
            return "Unknown command: " + chinese["未知命令：".Length..];
        }

        if (chinese.StartsWith("已恢复存档继续 · ", StringComparison.Ordinal))
        {
            return "Save restored · " + Tr(chinese["已恢复存档继续 · ".Length..]);
        }

        if (chinese.StartsWith("已报听 · 听 ", StringComparison.Ordinal))
        {
            return "Called · waiting: " + chinese["已报听 · 听 ".Length..];
        }

        if (chinese.StartsWith("荣和机会：别人打出 ", StringComparison.Ordinal))
        {
            return EnglishTiles("Ron chance: discarded " + chinese["荣和机会：别人打出 ".Length..].Replace("赋可和", " fu", StringComparison.Ordinal));
        }

        if (chinese.StartsWith("副露机会：别人打出 ", StringComparison.Ordinal))
        {
            return EnglishTiles("Meld chance: discarded " + chinese["副露机会：别人打出 ".Length..]);
        }

        if (chinese.StartsWith("报听错和风险：这手只有 ", StringComparison.Ordinal))
        {
            return "Misfire risk: only " + chinese["报听错和风险：这手只有 ".Length..]
                .Replace(" 赋（不足 ", " fu (below ", StringComparison.Ordinal)
                .Replace("）· 点荣和按错和罚", ") · Ron triggers penalty", StringComparison.Ordinal);
        }

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

        if (chinese.StartsWith("听 ", StringComparison.Ordinal))
        {
            return "待ち " + chinese["听 ".Length..]
                .Replace(" · 余 ", "・残り ", StringComparison.Ordinal)
                .Replace(" 张", "枚", StringComparison.Ordinal)
                .Replace(" · 打点 ", "・打点 ", StringComparison.Ordinal);
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

        return EnglishTiles(chinese);
    }

    private static string TrJapanesePatterns(string chinese)
    {
        if (chinese.StartsWith("第 ", StringComparison.Ordinal) && chinese.EndsWith(" 页", StringComparison.Ordinal))
        {
            return chinese[2..^2].Trim() + " ページ";
        }

        if (chinese.StartsWith("本机累计 · ", StringComparison.Ordinal) && chinese.EndsWith(" 个小局", StringComparison.Ordinal))
        {
            return "本機累計 · " + chinese["本机累计 · ".Length..^" 个小局".Length] + "局";
        }

        if (chinese.StartsWith("已解锁 ", StringComparison.Ordinal))
        {
            return "解除済み " + chinese["已解锁 ".Length..];
        }

        if (chinese.StartsWith("未知命令：", StringComparison.Ordinal))
        {
            return "不明なコマンド：" + chinese["未知命令：".Length..];
        }

        if (chinese.StartsWith("已恢复存档继续 · ", StringComparison.Ordinal))
        {
            return "セーブを復元・" + JapaneseRound(chinese["已恢复存档继续 · ".Length..]);
        }

        if (chinese.StartsWith("已报听 · 听 ", StringComparison.Ordinal))
        {
            return "宣言済み・待ち：" + chinese["已报听 · 听 ".Length..];
        }

        if (chinese.StartsWith("荣和机会：别人打出 ", StringComparison.Ordinal))
        {
            return JapaneseTiles("ロン可能：他家の打牌 " + chinese["荣和机会：别人打出 ".Length..].Replace("赋可和", "賦で和了", StringComparison.Ordinal));
        }

        if (chinese.StartsWith("副露机会：别人打出 ", StringComparison.Ordinal))
        {
            return JapaneseTiles("鳴き可能：他家の打牌 " + chinese["副露机会：别人打出 ".Length..]);
        }

        if (chinese.StartsWith("报听错和风险：这手只有 ", StringComparison.Ordinal))
        {
            return "誤宣言リスク：この手は " + chinese["报听错和风险：这手只有 ".Length..]
                .Replace(" 赋（不足 ", " 賦（", StringComparison.Ordinal)
                .Replace("）· 点荣和按错和罚", "）・ロンで誤宣言罰", StringComparison.Ordinal);
        }

        if (chinese.StartsWith("东", StringComparison.Ordinal)
            || chinese.StartsWith("南", StringComparison.Ordinal)
            || chinese.StartsWith("西", StringComparison.Ordinal)
            || chinese.StartsWith("北", StringComparison.Ordinal))
        {
            string round = JapaneseRound(chinese);
            if (round.Contains("局", StringComparison.Ordinal))
            {
                return round;
            }
        }

        return chinese;
    }

    private static string JapaneseRound(string chinese) =>
        chinese.Replace("东", "東", StringComparison.Ordinal)
            .Replace("南", "南", StringComparison.Ordinal)
            .Replace("西", "西", StringComparison.Ordinal)
            .Replace("北", "北", StringComparison.Ordinal)
            .Replace("本场", "本場", StringComparison.Ordinal)
            .Replace("场赏", "場賞", StringComparison.Ordinal);

    private static string EnglishTiles(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = System.Text.RegularExpressions.Regex.Replace(text, "([1-9])万", "$1m");
        text = System.Text.RegularExpressions.Regex.Replace(text, "([1-9])筒", "$1p");
        text = System.Text.RegularExpressions.Regex.Replace(text, "([1-9])索", "$1s");
        return text;
    }

    private static string JapaneseTiles(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = System.Text.RegularExpressions.Regex.Replace(text, "([1-9])万", "$1m");
        text = System.Text.RegularExpressions.Regex.Replace(text, "([1-9])筒", "$1p");
        text = System.Text.RegularExpressions.Regex.Replace(text, "([1-9])索", "$1s");
        return text;
    }
}
