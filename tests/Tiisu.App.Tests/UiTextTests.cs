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
