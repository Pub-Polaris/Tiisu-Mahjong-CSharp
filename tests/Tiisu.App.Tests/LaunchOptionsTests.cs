using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Parse_FlagsAndLanguage()
    {
        var options = LaunchOptions.Parse(new[] { "--cpu-render", "--fullscreen", "--lang", "en-US" });

        Assert.True(options.ForceCpuRender);
        Assert.True(options.Fullscreen);
        Assert.Equal("en-US", options.Language);
    }

    [Fact]
    public void Parse_DefaultsAndNormalization()
    {
        Assert.Equal(new LaunchOptions(false, false, null), LaunchOptions.Parse(System.Array.Empty<string>()));
        Assert.Equal("zh-CN", LaunchOptions.Parse(new[] { "--lang", "fr" }).Language);
        Assert.Equal("ja-JP", LaunchOptions.Parse(new[] { "--lang", "ja" }).Language);
        Assert.Null(LaunchOptions.Parse(new[] { "--lang" }).Language);
    }
}
