using Tiisu.App.Ui;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class UiTextTests
{
    [Fact]
    public void EnglishTranslationsAndFallback()
    {
        try
        {
            UiText.SetLanguage("en-US");
            Assert.Equal("Settings", UiText.Tr("设置"));
            Assert.Equal("Back to Menu", UiText.Tr("返回菜单"));
            Assert.Equal("Not translated", UiText.Tr("Not translated"));
            Assert.Equal("East 1 round / 4 round", UiText.Tr("东1局 / 4局"));
        Assert.Equal("Called · waiting: 3m 4p", UiText.Tr("已报听 · 听 3m 4p"));
        Assert.Equal("Unknown command: foo", UiText.Tr("未知命令：foo"));
        Assert.Equal("Waiting for other players", UiText.Tr("等待其他玩家确认"));
        Assert.Equal("Ron chance: discarded 8s · 3 fu", UiText.Tr("荣和机会：别人打出 8索 · 3赋可和"));
        Assert.Equal("Page 2 / 3", UiText.Tr("第 2 / 3 页"));
            Assert.Equal("20 s", UiText.Tr("20 秒"));
        }
        finally
        {
            UiText.SetLanguage("zh-CN");
        }
    }

    [Fact]
    public void ChineseReturnsOriginal()
    {
        UiText.SetLanguage("zh-CN");
        Assert.Equal("设置", UiText.Tr("设置"));
        Assert.Equal("en-US", UiText.Normalize("en"));
        Assert.Equal("zh-CN", UiText.Normalize("fr"));
    }
}
