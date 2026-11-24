using System;
using System.IO;
using System.Linq;
using Tiisu.App.Table;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class TableSkinTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tiisu.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void Blender_skin_manifest_loads_and_maps_regions()
    {
        string path = Path.Combine(RepoRoot(), "art", "table", "skin-blender", "skin.json");

        using TableSkin? skin = TableSkin.Load(path, out string? error);

        Assert.Null(error);
        Assert.NotNull(skin);
        Assert.True(skin!.IsActive);
        Assert.True(skin.Has(TableSkinRegion.Table));
        Assert.True(skin.Has(TableSkinRegion.CenterPlate));
        Assert.Equal(2, skin.RegionCount);

        Assert.True(skin.TryGetRegionSize(TableSkinRegion.CenterPlate, out float centerW, out float centerH));
        Assert.Equal(400f, centerW, 1);
        Assert.Equal(400f, centerH, 1);
    }

    [Fact]
    public void Missing_manifest_returns_null_and_error()
    {
        using TableSkin? skin = TableSkin.Load(
            Path.Combine(RepoRoot(), "art", "table", "skin-blender", "no-such-skin.json"),
            out string? error);

        Assert.Null(skin);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Newer_manifest_version_is_rejected()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-skin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "skin.json");
        try
        {
            File.WriteAllText(path, "{\"version\":999,\"regions\":{\"table\":{\"src\":[0,0,1,1]}}}");
            using TableSkin? skin = TableSkin.Load(path, out string? error);

            Assert.Null(skin);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // 测试清理失败不影响结果。
            }
        }
    }

    [Fact]
    public void Snapshot_types_have_no_geometry_fields()
    {
        string[] forbidden = { "vertex", "triangle", "mesh", "matrix", "transform" };
        var properties = typeof(TableViewModel).GetProperties()
            .Concat(typeof(SeatView).GetProperties());

        foreach (var property in properties)
        {
            foreach (string word in forbidden)
            {
                Assert.DoesNotContain(word, property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(word, property.PropertyType.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
