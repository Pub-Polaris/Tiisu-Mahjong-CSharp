using Xunit;

namespace Tiisu.Api.Tests;

/// <summary>路径穿越防线（审计 P0-1）。</summary>
public sealed class DataStorePathTests
{
    [Fact]
    public void SafeUnderRoot_普通子路径在根内()
    {
        string root = Path.Combine(Path.GetTempPath(), "tiisu-path-test");

        string path = DataStore.SafeUnderRoot(root, "logs", "abc", "events.jsonl");

        Assert.StartsWith(Path.GetFullPath(root), path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("events.jsonl", path);
    }

    [Fact]
    public void SafeUnderRoot_点号上跳被拒绝()
    {
        string root = Path.Combine(Path.GetTempPath(), "tiisu-path-test");

        Assert.Throws<InvalidOperationException>(
            () => DataStore.SafeUnderRoot(root, ".."));
        Assert.Throws<InvalidOperationException>(
            () => DataStore.SafeUnderRoot(root, "..", "outside.txt"));
    }

    [Fact]
    public void SafeUnderRoot_绝对路径被拒绝()
    {
        string root = Path.Combine(Path.GetTempPath(), "tiisu-path-test");
        string outside = Path.Combine(Path.GetTempPath(), "tiisu-outside.txt");

        Assert.Throws<InvalidOperationException>(
            () => DataStore.SafeUnderRoot(root, outside));
    }

    [Fact]
    public void Sanitize_不保留路径分隔符()
    {
        Assert.DoesNotContain("/", DataStore.Sanitize("a/b"));
        Assert.DoesNotContain("\\", DataStore.Sanitize("a\\b"));
        Assert.DoesNotContain(":", DataStore.Sanitize("C:x"));
    }
}
