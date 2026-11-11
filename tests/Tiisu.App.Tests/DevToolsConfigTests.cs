using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>开发命令开关（审计 P3-5）：JSON 配置、默认关闭、可手动改。</summary>
public sealed class DevToolsConfigTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-devtools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void 文件缺失时默认关闭()
    {
        string dir = TempDir();
        try
        {
            Assert.False(DevToolsConfig.Enabled(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void enabled为true时打开()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "devtools.json"), """{"enabled":true}""");

            Assert.True(DevToolsConfig.Enabled(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 模板默认关闭且可读()
    {
        string dir = TempDir();
        try
        {
            DevToolsConfig.EnsureTemplate(dir);

            Assert.True(File.Exists(Path.Combine(dir, "devtools.json")));
            Assert.False(DevToolsConfig.Enabled(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
