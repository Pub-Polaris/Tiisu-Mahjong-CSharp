using System;
using System.IO;
using System.Linq;
using System.Text;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class PlayerProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "tiisu-profile-tests",
        Guid.NewGuid().ToString("N"));

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
            // 测试清理失败不影响结果。
        }
    }

    [Fact]
    public void First_run_creates_one_active_profile_with_settings()
    {
        PlayerProfileStore store = PlayerProfileStore.LoadOrCreate(_root);

        Assert.Single(store.Profiles);
        Assert.Equal(store.Profiles[0].Id, store.Active.Id);
        Assert.True(Directory.Exists(store.Active.Directory));
        Assert.True(File.Exists(Path.Combine(store.Active.Directory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(store.Active.Directory, "profile.json")));
    }

    [Fact]
    public void Create_switch_rename_delete_and_reload()
    {
        PlayerProfileStore store = PlayerProfileStore.LoadOrCreate(_root);
        PlayerProfile a = store.Active;
        PlayerProfile b = store.Create("第二个", activate: false);

        Assert.Equal(a.Id, store.Active.Id);
        store.SetActive(b.Id);
        Assert.Equal(b.Id, store.Active.Id);

        PlayerProfile renamed = store.Rename(b.Id, "新名字");
        Assert.Equal("新名字", renamed.DisplayName);

        PlayerProfileStore reloaded = PlayerProfileStore.LoadOrCreate(_root);
        Assert.Equal(2, reloaded.Profiles.Count);
        Assert.Equal(b.Id, reloaded.Active.Id);
        Assert.Equal("新名字", reloaded.Find(b.Id)!.DisplayName);

        reloaded.Delete(a.Id);
        Assert.Single(reloaded.Profiles);
        Assert.True(Directory.Exists(Path.Combine(reloaded.ProfilesDirectory, "_trash")));

        Assert.Throws<InvalidOperationException>(() => reloaded.Delete(reloaded.Active.Id));
    }

    [Fact]
    public void Legacy_flat_data_is_copied_into_default_profile()
    {
        Directory.CreateDirectory(Path.Combine(_root, "logs"));
        File.WriteAllText(Path.Combine(_root, "settings.json"),
            "{\"language\":\"en-US\",\"playerName\":\"旧玩家\",\"recordTiles\":true}", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "stats.json"),
            "{\"rounds\":7,\"wins\":3,\"byPlayer\":{}}", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "logs", "roundlog_20260101.jsonl"), "{}\n", Encoding.UTF8);

        PlayerProfileStore store = PlayerProfileStore.LoadOrCreate(_root);

        PlayerProfile profile = Assert.Single(store.Profiles);
        Assert.Equal("旧玩家", profile.DisplayName);
        Assert.True(File.Exists(Path.Combine(profile.Directory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(profile.Directory, "stats.json")));
        Assert.True(File.Exists(Path.Combine(profile.Directory, "logs", "roundlog_20260101.jsonl")));
        Assert.True(File.Exists(Path.Combine(_root, "settings.json"))); // 旧文件保留
        Assert.Contains("\"rounds\":7", File.ReadAllText(Path.Combine(profile.Directory, "stats.json")));
    }

    [Fact]
    public void Corrupt_index_is_rebuilt_from_profile_files()
    {
        PlayerProfileStore store = PlayerProfileStore.LoadOrCreate(_root);
        PlayerProfile second = store.Create("B", activate: false);
        File.WriteAllText(store.IndexPath, "{ not json", Encoding.UTF8);

        PlayerProfileStore rebuilt = PlayerProfileStore.LoadOrCreate(_root);

        Assert.Equal(2, rebuilt.Profiles.Count);
        Assert.NotNull(rebuilt.Find(second.Id));
        Assert.True(File.Exists(store.IndexPath));
        Assert.True(Directory.EnumerateFiles(_root, "profiles.json.bak-*").Any());
    }

    [Fact]
    public void Launch_override_creates_profile_without_changing_active()
    {
        PlayerProfileStore store = PlayerProfileStore.LoadOrCreate(_root);
        PlayerProfile active = store.Active;

        PlayerProfile overridden = store.ResolveLaunchProfile("测试者");

        Assert.Equal("测试者", overridden.DisplayName);
        Assert.Equal(active.Id, store.Active.Id);
        Assert.Contains(store.Profiles, p => p.Id == overridden.Id);
    }

    [Fact]
    public void Steam_identity_is_idempotent()
    {
        PlayerProfileStore store = PlayerProfileStore.LoadOrCreate(_root);

        PlayerProfile first = store.EnsureSteamProfile("76561198000000000", "Steam 玩家");
        PlayerProfile second = store.EnsureSteamProfile("76561198000000000", "改了名");

        Assert.Equal(first.Id, second.Id);
        Assert.True(first.IsSteam);
        Assert.Equal(first.Id, store.FindBySteamId("76561198000000000")!.Id);
    }

    [Fact]
    public void Names_are_trimmed_and_clamped()
    {
        Assert.Equal("玩家 1", PlayerProfileStore.NormalizeName("   ", 1));
        Assert.Equal("a b", PlayerProfileStore.NormalizeName("  a   b  ", 1));
        Assert.Equal(PlayerProfileStore.MaxNameLength, PlayerProfileStore.NormalizeName(new string('x', 40), 1).Length);
    }
}
