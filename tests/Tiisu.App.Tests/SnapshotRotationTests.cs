using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class SnapshotRotationTests
{
    [Fact]
    public void Viewer_seat_is_rotated_to_bottom()
    {
        var vm = new TableViewModel
        {
            TurnIndex = 3,
            Seats = new[]
            {
                new SeatView { Index = 0, Name = "S0" },
                new SeatView { Index = 1, Name = "S1" },
                new SeatView { Index = 2, Name = "S2", Hand = new[] { new TileView("1m") } },
                new SeatView { Index = 3, Name = "S3" },
            },
        };

        TableViewModel rotated = SnapshotRotation.RotateForViewer(vm, 2);

        Assert.Equal(0, rotated.SelfSeat);
        Assert.Equal("S2", rotated.Seats[0].Name);
        Assert.Equal("S3", rotated.Seats[1].Name);
        Assert.Equal("S0", rotated.Seats[2].Name);
        Assert.Equal("S1", rotated.Seats[3].Name);
        Assert.True(rotated.Seats[0].IsHuman);
        Assert.False(rotated.Seats[1].IsHuman);
        Assert.Equal(1, rotated.TurnIndex);
        Assert.Single(rotated.Seats[0].Hand);
    }

    [Fact]
    public void Absolute_index_survives_rotation_and_maps_back_to_winner()
    {
        var vm = new TableViewModel
        {
            Seats = new[]
            {
                new SeatView { Index = 0, AbsoluteIndex = 0, Name = "S0" },
                new SeatView { Index = 1, AbsoluteIndex = 1, Name = "S1" },
                new SeatView { Index = 2, AbsoluteIndex = 2, Name = "S2" },
                new SeatView { Index = 3, AbsoluteIndex = 3, Name = "S3" },
            },
        };

        TableViewModel rotated = SnapshotRotation.RotateForViewer(vm, 2);

        // 旋转只改显示序号，绝对座位必须保留。
        Assert.Equal(2, rotated.Seats[0].AbsoluteIndex);
        Assert.Equal(0, rotated.Seats[2].AbsoluteIndex);
        Assert.Equal(2, rotated.Seats[2].Index);

        // 结算字段用的是绝对座位：win=0 在 viewer=2 的屏幕上应落到显示序 2。
        Assert.Equal("S0", SeatIndexMap.ByAbsolute(rotated, 0)!.Name);
        Assert.Equal(2, SeatIndexMap.DisplayIndex(rotated, 0));
        Assert.Equal("S2", SeatIndexMap.ByAbsolute(rotated, 2)!.Name);
    }

    [Fact]
    public void Absolute_lookup_falls_back_to_index_for_legacy_snapshots()
    {
        var vm = new TableViewModel
        {
            Seats = new[]
            {
                new SeatView { Index = 0, Name = "S0" },
                new SeatView { Index = 1, Name = "S1" },
            },
        };

        Assert.Equal("S1", SeatIndexMap.ByAbsolute(vm, 1)!.Name);
        Assert.Null(SeatIndexMap.ByAbsolute(vm, 3));
        Assert.Equal(-1, SeatIndexMap.DisplayIndex(vm, 3));
    }
}
