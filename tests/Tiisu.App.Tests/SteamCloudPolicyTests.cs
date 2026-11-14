using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class SteamCloudPolicyTests
{
    [Fact]
    public void OnlySmallConfigFilesAreSynced()
    {
        Assert.True(SteamCloudPolicy.ShouldSync("settings.json"));
        Assert.True(SteamCloudPolicy.ShouldSync("C:/x/stats.json"));
        Assert.True(SteamCloudPolicy.ShouldSync("layout-custom.json"));
        Assert.False(SteamCloudPolicy.ShouldSync("logs/roundlog_20260101.jsonl"));
        Assert.False(SteamCloudPolicy.ShouldSync("voices/tingpai.wav"));
        Assert.False(SteamCloudPolicy.ShouldSync("steam-pending.json"));
        Assert.False(SteamCloudPolicy.ShouldSync(null));
    }

    [Fact]
    public void ExistingFiles_FiltersMissing()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tiisu-cloud-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "settings.json"), "{}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "voices.txt"), "x");
            var files = SteamCloudPolicy.ExistingFiles(dir);
            Assert.Single(files);
            Assert.EndsWith("settings.json", files[0]);
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }
}
