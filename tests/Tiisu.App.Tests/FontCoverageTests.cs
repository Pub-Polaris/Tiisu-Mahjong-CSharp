using System.IO;
using SkiaSharp;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class FontCoverageTests
{
    [Fact]
    public void EmbeddedFont_CoversVersionAndUiText()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "TiisuRoundedSC-subset.ttf");
        Assert.True(File.Exists(path), "字体子集未复制到输出目录：" + path);

        using var typeface = SKTypeface.FromFile(path);
        Assert.NotNull(typeface);
        using var font = new SKFont(typeface);

        // 版本号里的代号（北斗）也必须在字体里，否则主菜单版本行会缺字。
        string required = "北斗" + BuildInfo.Version + "开源信息许可参考新手引导设置下一局和牌荣和报听";
        foreach (char ch in required)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            Assert.True(font.ContainsGlyph(ch), "字体缺字：" + ch);
        }
    }
}
