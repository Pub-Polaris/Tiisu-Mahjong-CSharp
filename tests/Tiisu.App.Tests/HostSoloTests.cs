using System.Threading.Tasks;
using Tiisu.App.Runtime;
using Tiisu.Net;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class HostSoloTests
{
    [Fact]
    public async Task Host_can_start_alone_with_ai_fill()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var room = RoomInfo.Create("单机测试", "Host", 0, "test");
            using var host = new HostEngine(new Settings(), room, port: 0);
            host.StartServer();
            host.StartMatch(1);

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && host.Phase == MatchPhase.Idle)
            {
                await Task.Delay(20);
            }

            Assert.True(host.Phase is MatchPhase.HumanDiscard or MatchPhase.HumanRon
                or MatchPhase.HumanMeld or MatchPhase.RoundEnded,
                $"host did not start: {host.Phase}; {host.LastError}");
            Assert.Equal(NetworkConstants.MaxPlayers, host.LatestSnapshot.Seats.Count);
            Assert.True(host.LatestSnapshot.Seats[0].IsHuman);
            Assert.Contains(host.LatestSnapshot.Seats.Skip(1), seat => !seat.IsHuman);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
