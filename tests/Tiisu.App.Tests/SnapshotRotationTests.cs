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
}
