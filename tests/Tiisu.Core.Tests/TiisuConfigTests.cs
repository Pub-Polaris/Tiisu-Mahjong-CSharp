using Tiisu.Core;
using Xunit;

namespace Tiisu.Core.Tests;

public sealed class TiisuConfigTests
{
    [Fact]
    public void Parse_ReadsSectionsAndValues()
    {
        var config = TiisuConfig.Parse("""
            [debug]
            log_keys = true
            play_trace = true

            [perf]
            enabled = true

            [ai]
            policy_weights = "D:/weights.json"

            [dev]
            root = "E:/repo"
            """);

        Assert.True(config.LogKeys);
        Assert.True(config.PlayTrace);
        Assert.True(config.PerfEnabled);
        Assert.Equal("D:/weights.json", config.PolicyWeights);
        Assert.Equal("E:/repo", config.DevRoot);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndUnknownKeys()
    {
        var config = TiisuConfig.Parse("""
            # 注释
            [unknown]
            whatever = true

            [debug]
            log_keys = false # 行尾注释
            """);

        Assert.False(config.LogKeys);
        Assert.False(config.PerfEnabled);
    }

    [Fact]
    public void DefaultToml_ParsesToDisabled()
    {
        var config = TiisuConfig.Parse(TiisuConfig.DefaultToml);

        Assert.False(config.LogKeys);
        Assert.False(config.PlayTrace);
        Assert.False(config.PerfEnabled);
        Assert.Equal(string.Empty, config.PolicyWeights);
    }
}