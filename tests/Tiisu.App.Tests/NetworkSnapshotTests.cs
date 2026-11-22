using System.Text.Json;
using Tiisu.App.Table;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class NetworkSnapshotTests
{
    [Fact]
    public void NetworkSnapshotRoundTripsOwnHandOnly()
    {
        var snapshot = new TableViewModel
        {
            Phase = "human_discard",
            Revision = 7,
            RoundLabel = "东1局",
            Seats = new[]
            {
                new SeatView
                {
                    Index = 0,
                    Name = "Host",
                    IsHuman = false,
                    Hand = Array.Empty<TileView>(),
                },
                new SeatView
                {
                    Index = 1,
                    Name = "Client",
                    IsHuman = true,
                    Hand = new[] { new TileView("1m"), new TileView("2m") },
                },
            },
        };

        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        TableViewModel? restored = JsonSerializer.Deserialize<TableViewModel>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });

        Assert.NotNull(restored);
        Assert.Equal(7, restored!.Revision);
        Assert.Equal("human_discard", restored.Phase);
        Assert.Empty(restored.Seats[0].Hand);
        Assert.Equal(2, restored.Seats[1].Hand.Count);
        Assert.True(restored.Seats[1].IsHuman);
    }
}
