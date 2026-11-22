using System;
using System.IO;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class SettingsProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiisu-settings-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void Settings_save_and_load_stay_inside_profile_directory()
    {
        string a = Path.Combine(_root, "a");
        string b = Path.Combine(_root, "b");

        Settings first = Settings.Load(a);
        first.Language = "en-US";
        first.Save();

        Assert.True(File.Exists(Path.Combine(a, "settings.json")));
        Assert.False(File.Exists(Path.Combine(b, "settings.json")));

        Settings reloadedA = Settings.Load(a);
        Assert.Equal("en-US", reloadedA.Language);
        Assert.Equal(Path.GetFullPath(a), reloadedA.DataDirectory);

        Settings reloadedB = Settings.Load(b);
        Assert.Equal("zh-CN", reloadedB.Language);
        Assert.Equal(Path.GetFullPath(b), reloadedB.DataDirectory);
    }

    [Fact]
    public void AppData_current_is_used_by_default_stores()
    {
        string profile = Path.Combine(_root, "profile");
        string previous = AppData.Current;
        try
        {
            AppData.Use(profile);
            var stats = new StatsStore();
            Assert.Equal(Path.GetFullPath(profile), stats.DirectoryPath);
        }
        finally
        {
            AppData.Use(previous);
        }
    }
}
