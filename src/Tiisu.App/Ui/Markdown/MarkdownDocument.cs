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

public sealed record MarkdownBlock(
    MarkdownBlockKind Kind,
    string Text,
    int Level = 0,
    bool Header = false);

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

            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, Inline(string.Join(" ", paragraph))));
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
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Quote, Inline(line.TrimStart()[1..].TrimStart())));
                continue;
            }

            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Bullet, Inline(bullet.Groups[1].Value)));
                continue;
            }

            var ordered = Ordered.Match(line);
            if (ordered.Success)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Bullet, Inline(ordered.Groups[1].Value)));
                continue;
            }

            if (line.Contains('|') && i + 1 < lines.Length && TableSeparator.IsMatch(lines[i + 1]))
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Table, Inline(ParseRow(line)), Header: true));
                i++;
                while (i + 1 < lines.Length && lines[i + 1].Contains('|') && !string.IsNullOrWhiteSpace(lines[i + 1]))
                {
                    i++;
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Table, Inline(ParseRow(lines[i]))));
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

    private static string ParseRow(string line)
    {
        string trimmed = line.Trim().Trim('|');
        var cells = trimmed.Split('|').Select(c => c.Trim());
        return string.Join("    ", cells.Where(c => c.Length > 0));
    }

    /// <summary>去掉行内 Markdown 标记；链接保留「文字 (URL)」。</summary>
    private static string Inline(string text)
    {
        string s = text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
        s = Regex.Replace(s, @"!\[([^\]]*)\]\(([^)]*)\)", "$1");
        s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");
        s = Regex.Replace(s, @"<[^>]+>", string.Empty);
        return s.Trim();
    }
}
