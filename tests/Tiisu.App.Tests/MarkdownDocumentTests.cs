using Tiisu.App.Ui.Markdown;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class MarkdownDocumentTests
{
    [Fact]
    public void Parse_HeadingsListsAndCode()
    {
        var blocks = MarkdownDocument.Parse("""
            # 标题

            一段正文 **加粗** 和 `代码`。

            - 第一条
            - 第二条

            ```csharp
            var x = 1;
            ```
            """);

        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Heading && b.Level == 1);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Bullet);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Code && b.Text.Contains("var x"));
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Paragraph && !b.Text.Contains("**"));
    }

    [Fact]
    public void Parse_TableAndQuote()
    {
        var blocks = MarkdownDocument.Parse("""
            > 引用

            | A | B |
            |---|---|---|
            | 1 | 2 |
            """);

        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Quote);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Table && b.Header);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Table && !b.Header);
    }

    [Fact]
    public void Parse_Empty()
    {
        Assert.Empty(MarkdownDocument.Parse(""));
        Assert.Empty(MarkdownDocument.Parse(null));
    }
}