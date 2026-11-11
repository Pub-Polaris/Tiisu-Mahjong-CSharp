using System.Text.Json.Nodes;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>前端第一组测试（审计 P1-7）：设置文件的命名、原子写与回读。</summary>
public sealed class SettingsTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save_写camelCase与Web一致()
    {
        string dir = TempDir();
        try
        {
            var settings = new Settings
            {
                ThinkSeconds = 17,
                RecordTiles = true,
                ShowMa = true,
                Ruleset = Tiisu.Core.Ruleset.TuidaoHu,
            };

            settings.Save(dir);

            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "settings.json")))!.AsObject();
            Assert.Equal(17, (int)json["thinkSeconds"]!);
            Assert.True((bool)json["recordTiles"]!);
            Assert.True((bool)json["showMa"]!);
            Assert.Equal(1, (int)json["ruleset"]!);
            Assert.False(json.ContainsKey("ThinkSeconds"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save再Load_字段保持()
    {
        string dir = TempDir();
        try
        {
            var settings = new Settings
            {
                ThinkSeconds = 9,
                BgmVolume = 55,
                SfxVolume = 25,
                OptionalYaku = new List<string> { "all_number" },
            };

            settings.Save(dir);
            var loaded = Settings.Load(dir);

            Assert.Equal(9, loaded.ThinkSeconds);
            Assert.Equal(55, loaded.BgmVolume);
            Assert.Equal(25, loaded.SfxVolume);
            Assert.Contains("all_number", loaded.OptionalYaku);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_不留下tmp文件()
    {
        string dir = TempDir();
        try
        {
            new Settings().Save(dir);

            Assert.False(File.Exists(Path.Combine(dir, "settings.json.tmp")));
            Assert.True(File.Exists(Path.Combine(dir, "settings.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_坏文件改名corrupt并回默认()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ broken");
            var loaded = Settings.Load(dir);

            Assert.Equal(30, loaded.ThinkSeconds);
            Assert.True(File.Exists(Path.Combine(dir, "settings.json.corrupt")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
