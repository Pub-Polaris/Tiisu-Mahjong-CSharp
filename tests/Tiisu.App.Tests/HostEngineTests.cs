using System.Text.Json;
using System.Threading.Tasks;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Tiisu.Net;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class HostEngineTests
{
    [Fact]
    public async Task Host_starts_four_player_match_and_sends_per_seat_snapshots()
    {
        var settings = new Settings();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
        var room = RoomInfo.Create("测试房", "Host", 0, "test");
        using var host = new HostEngine(settings, room, port: 0);
        host.StartServer();

        var clients = new List<PeerClient>();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                clients.Add(await PeerClient.ConnectAsync("127.0.0.1", "C" + i, "test", host.Port));
            }

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (host.Lobby.Count < 4 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(4, host.Lobby.Count);
            var snapshots = new TaskCompletionSource<SnapshotMessage>[3];
            for (int i = 0; i < clients.Count; i++)
            {
                int index = i;
                snapshots[index] = new TaskCompletionSource<SnapshotMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                PeerClient client = clients[index];
                client.SnapshotReceived += message => snapshots[index].TrySetResult(message);
            }

            host.StartMatch(1);

            var received = new List<SnapshotMessage>();
            foreach (var source in snapshots)
            {
                Task completed = await Task.WhenAny(source.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.True(completed == source.Task,
                    $"no snapshot; phase={host.Phase}, error={host.LastError}, revision={host.LatestSnapshot.Revision}");
                received.Add(await source.Task);
            }

            Assert.Equal(3, received.Select(s => s.Seat).Distinct().Count());
            foreach (SnapshotMessage message in received)
            {
                using var document = JsonDocument.Parse(message.Snapshot);
                JsonElement root = document.RootElement;
                JsonElement seats = root.GetProperty("seats");
                JsonElement own = seats[0];
                Assert.True(own.GetProperty("isHuman").GetBoolean());
                Assert.True(own.GetProperty("hand").GetArrayLength() > 0);
                for (int seat = 0; seat < seats.GetArrayLength(); seat++)
                {
                    if (seat != 0)
                    {
                        Assert.Equal(0, seats[seat].GetProperty("hand").GetArrayLength());
                    }
                }
            }
        }
        finally
        {
            foreach (PeerClient client in clients)
            {
                await client.DisposeAsync();
            }
        }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
