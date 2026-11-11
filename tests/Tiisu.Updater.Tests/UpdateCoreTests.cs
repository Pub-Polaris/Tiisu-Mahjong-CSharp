using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace Tiisu.Updater.Tests;

public sealed class UpdateCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiisu-updater-tests-" + Guid.NewGuid().ToString("N"));

    public UpdateCoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 测试清理失败忽略
        }
    }

    private string Dir(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string dir, string rel, string text = "x")
    {
        string p = Path.Combine(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    [Fact]
    public void CopyTree_DirectoryPayload_CopiesNewAndKeepsOld()
    {
        string payload = Dir("payload");
        string install = Dir("install");
        Write(payload, "Tiisu.Mahjong.exe", "new");
        Write(payload, "new.txt", "new");
        Write(install, "old.txt", "old");

        UpdateCore.CopyTree(payload, install);

        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "Tiisu.Mahjong.exe")));
        Assert.True(File.Exists(Path.Combine(install, "new.txt")));
        Assert.True(File.Exists(Path.Combine(install, "old.txt")));
    }

    [Fact]
    public void ResolvePayload_ZipWithNestedFolder_FindsAppDir()
    {
        string payload = Dir("payload-src");
        Write(payload, Path.Combine("Tiisu-v2", "Tiisu.Mahjong.exe"), "exe");
        string zip = Path.Combine(_root, "update.zip");
        ZipFile.CreateFromDirectory(payload, zip);

        string resolved = UpdateCore.ResolvePayload(zip, out string? temp);
        try
        {
            Assert.EndsWith("Tiisu-v2", resolved.TrimEnd(Path.DirectorySeparatorChar));
            Assert.True(File.Exists(Path.Combine(resolved, "Tiisu.Mahjong.exe")));
        }
        finally
        {
            if (temp is not null && Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public void Backup_DoesNotRecurseIntoBackupFolder()
    {
        string install = Dir("install");
        Write(install, "app.txt", "v1");
        Write(install, Path.Combine("backup", "update_20260101_000000", "old.txt"), "old");

        string backup = UpdateCore.Backup(install, keep: 3);

        Assert.True(File.Exists(Path.Combine(backup, "app.txt")));
        Assert.False(Directory.Exists(Path.Combine(backup, "backup")));
    }

    [Fact]
    public void PruneBackups_KeepsNewestThree()
    {
        string install = Dir("install");
        string backupRoot = Path.Combine(install, "backup");
        foreach (string name in new[] { "update_20260101_000000", "update_20260102_000000", "update_20260103_000000", "update_20260104_000000" })
        {
            Directory.CreateDirectory(Path.Combine(backupRoot, name));
        }

        UpdateCore.PruneBackups(backupRoot, keep: 3);

        var left = Directory.GetDirectories(backupRoot).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(3, left.Length);
        Assert.DoesNotContain("update_20260101_000000", left);
    }

    [Fact]
    public void CopyTree_SkipUpdaterFiles_KeepsOldUpdater()
    {
        string payload = Dir("payload");
        string install = Dir("install");
        Write(payload, "Tiisu.Updater.exe", "new-updater");
        Write(payload, "Tiisu.Updater.dll", "new-updater-dll");
        Write(payload, "Tiisu.Mahjong.exe", "new-app");
        Write(install, "Tiisu.Updater.exe", "old-updater");

        UpdateCore.CopyTree(payload, install, skipUpdaterFiles: true);

        Assert.Equal("old-updater", File.ReadAllText(Path.Combine(install, "Tiisu.Updater.exe")));
        Assert.False(File.Exists(Path.Combine(install, "Tiisu.Updater.dll")));
        Assert.Equal("new-app", File.ReadAllText(Path.Combine(install, "Tiisu.Mahjong.exe")));
    }

    [Fact]
    public void WriteVersion_MergesPayloadAndPreservesJsonTypes()
    {
        string payload = Dir("payload");
        string install = Dir("install");
        File.WriteAllText(Path.Combine(payload, "version.json"),
            "{\"version\":\"261112-abc\",\"build\":123,\"flag\":true}");

        UpdateCore.WriteVersion(install, "src.zip", payload);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(install, "version.json")));
        var root = doc.RootElement;
        Assert.Equal("261112-abc", root.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("build").ValueKind);
        Assert.Equal(123, root.GetProperty("build").GetInt32());
        Assert.Equal(JsonValueKind.True, root.GetProperty("flag").ValueKind);
        Assert.Equal("src.zip", root.GetProperty("source").GetString());
        Assert.Equal(UpdateCore.AppExe, root.GetProperty("app").GetString());
        Assert.True(root.TryGetProperty("updatedAt", out _));
    }

    [Fact]
    public void WriteVersion_BrokenPayloadVersion_StillWrites()
    {
        string payload = Dir("payload");
        string install = Dir("install");
        File.WriteAllText(Path.Combine(payload, "version.json"), "{not json");

        UpdateCore.WriteVersion(install, "src", payload);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(install, "version.json")));
        Assert.Equal("src", doc.RootElement.GetProperty("source").GetString());
    }
}
