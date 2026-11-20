using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tiisu.App.Ui.Markdown;

public enum MarkdownBlockKind
{
    Heading,
    Paragraph,
    Bullet,
    Quote,
    Code,
    Table,
    Rule,
}

public enum MarkdownInlineKind
{
    Text,
    Code,
}

/// <summary>行内片段：普通文字或反引号代码 span。Code 片段保留原文，供牌型渲染识别。</summary>
public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text);

public sealed record MarkdownBlock(
    MarkdownBlockKind Kind,
    string Text,
    int Level = 0,
    bool Header = false,
    IReadOnlyList<string>? Cells = null,
    IReadOnlyList<MarkdownInline>? Inlines = null);

/// <summary>
/// 游戏内文档用的 Markdown 子集解析器：标题 / 段落 / 列表 / 引用 / 代码块 / 简单表格。
/// 不依赖第三方库；行内样式只保留文字，不做富文本渲染。
/// </summary>
public static class MarkdownDocument
{
    private static readonly Regex Heading = new(@"^(#{1,4})\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^[-*+]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex Ordered = new(@"^\d+[.)]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new(@"^\s*\|?[\s:|-]+\|[\s:|-]*$", RegexOptions.Compiled);
    private static readonly Regex CodeSpan = new(@"`([^`\n]+)`", RegexOptions.Compiled);

    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrEmpty(markdown))
        {
            return blocks;
        }

        string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        bool inCode = false;
        var code = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            string raw = string.Join(" ", paragraph);
            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, Inline(raw),
                Inlines: ParseInlines(raw)));
            paragraph.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string line = raw.TrimEnd();

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                if (inCode)
                {
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, string.Join("\n", code)));
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }

                continue;
            }

            if (inCode)
            {
                code.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            if (Rule.IsMatch(line))
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, string.Empty));
                continue;
            }

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, Inline(heading.Groups[2].Value), heading.Groups[1].Length));
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                FlushParagraph();
                string quote = line.TrimStart()[1..].TrimStart();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Quote, Inline(quote),
                    Inlines: ParseInlines(quote)));
                continue;
            }

            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                string text = bullet.Groups[1].Value;
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Bullet, Inline(text),
                    Inlines: ParseInlines(text)));
                continue;
            }

            var ordered = Ordered.Match(line);
            if (ordered.Success)
            {
                FlushParagraph();
                string text = ordered.Groups[1].Value;
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Bullet, Inline(text),
                    Inlines: ParseInlines(text)));
                continue;
            }

            if (line.Contains('|') && i + 1 < lines.Length && TableSeparator.IsMatch(lines[i + 1]))
            {
                FlushParagraph();
                var cells = ParseCells(line);
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Table, string.Join("    ", cells),
                    Header: true, Cells: cells));
                i++;
                while (i + 1 < lines.Length && lines[i + 1].Contains('|') && !string.IsNullOrWhiteSpace(lines[i + 1]))
                {
                    i++;
                    var rowCells = ParseCells(lines[i]);
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Table, string.Join("    ", rowCells),
                        Cells: rowCells));
                }

                continue;
            }

            paragraph.Add(line.Trim());
        }

        FlushParagraph();
        if (inCode && code.Count > 0)
        {
            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, string.Join("\n", code)));
        }

        return blocks;
    }

    public static string PlainText(string? markdown) =>
        string.Join("\n", Parse(markdown).Select(b => b.Text));

    private static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        var result = new List<MarkdownInline>();
        int start = 0;
        foreach (Match match in CodeSpan.Matches(text))
        {
            if (match.Index > start)
            {
                string before = Inline(text[start..match.Index]);
                if (before.Length > 0)
                {
                    result.Add(new MarkdownInline(MarkdownInlineKind.Text, before));
                }
            }

            string code = match.Groups[1].Value.Trim();
            if (code.Length > 0)
            {
                result.Add(new MarkdownInline(MarkdownInlineKind.Code, code));
            }

            start = match.Index + match.Length;
        }

        if (start < text.Length)
        {
            string after = Inline(text[start..]);
            if (after.Length > 0)
            {
                result.Add(new MarkdownInline(MarkdownInlineKind.Text, after));
            }
        }

        if (result.Count == 0)
        {
            string plain = Inline(text);
            if (plain.Length > 0)
            {
                result.Add(new MarkdownInline(MarkdownInlineKind.Text, plain));
            }
        }

        return result;
    }

    private static string ParseRow(string line)
    {
        string trimmed = line.Trim().Trim('|');
        var cells = trimmed.Split('|').Select(c => c.Trim());
        return string.Join("    ", cells.Where(c => c.Length > 0));
    }

    private static IReadOnlyList<string> ParseCells(string line)
    {
        string trimmed = line.Trim().Trim('|');
        return trimmed.Split('|')
            .Select(c => Inline(c.Trim()))
            .Where(c => c.Length > 0)
            .ToArray();
    }

    /// <summary>去掉行内 Markdown 标记；链接保留「文字 (URL)」。</summary>
    private static string Inline(string text)
    {
        string s = text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
        // 圆体子集没有 emoji 字形；用文字标记代替，避免 Markdown 页出现空框。
        s = s.Replace("✅", "[OK]").Replace("❌", "[X]").Replace("⚠️", "[!]").Replace("⚠", "[!]").Replace("\uFE0F", string.Empty);
        s = Regex.Replace(s, @"!\[([^\]]*)\]\(([^)]*)\)", "$1");
        s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");
        s = Regex.Replace(s, @"<[^>]+>", string.Empty);
        return s.Trim();
    }
}
