using System;
using System.IO;
using System.Text;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class SteamCloudSyncTests
{
    private static string TempDir(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-cloudsync-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Sync_UploadsThenSkipsIdentical()
    {
        string local = TempDir("local");
        string remote = TempDir("remote");
        try
        {
            File.WriteAllText(Path.Combine(local, "settings.json"), "{\"a\":1}", Encoding.UTF8);
            var cloud = new LocalCloudStorage(remote);
            var sync = new SteamCloudSync();

            var first = sync.Sync(local, cloud);
            Assert.True(first.IsSuccess);
            Assert.Equal(1, first.Uploaded);
            Assert.Equal(0, first.Downloaded);

            var second = sync.Sync(local, cloud);
            Assert.Equal(0, second.Uploaded);
            Assert.Equal(1, second.Skipped);
        }
        finally
        {
            Directory.Delete(local, recursive: true);
            Directory.Delete(remote, recursive: true);
        }
    }

    [Fact]
    public void Sync_DownloadsMissingLocalAndLocalWinsConflict()
    {
        string local = TempDir("local");
        string remote = TempDir("remote");
        try
        {
            var cloud = new LocalCloudStorage(remote);
            cloud.Write("stats.json", Encoding.UTF8.GetBytes("cloud"));
            cloud.Write("steam-pending.json", Encoding.UTF8.GetBytes("ignore"));

            var sync = new SteamCloudSync();
            var result = sync.Sync(local, cloud);

            Assert.Equal(1, result.Downloaded);
            Assert.Equal("cloud", File.ReadAllText(Path.Combine(local, "stats.json")));
            Assert.False(File.Exists(Path.Combine(local, "steam-pending.json")));

            File.WriteAllText(Path.Combine(local, "stats.json"), "local", new UTF8Encoding(false));
            var conflict = sync.Sync(local, cloud);
            Assert.Equal(1, conflict.Uploaded);
            Assert.Equal("local", new UTF8Encoding(false).GetString(cloud.Read("stats.json")!));
        }
        finally
        {
            Directory.Delete(local, recursive: true);
            Directory.Delete(remote, recursive: true);
        }
    }
}
