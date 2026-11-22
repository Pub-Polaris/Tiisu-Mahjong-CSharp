using System.Threading.Tasks;
using Tiisu.App.Runtime;
using Tiisu.Net;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class HostCheatTests
{
    [Fact]
    public async Task Wrong_snapshot_hash_cancels_the_match()
    {
        var settings = new Settings();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var clients = new List<PeerClient>();
        try
        {
            var room = RoomInfo.Create("作弊房", "Host", 0, "test");
            using var host = new HostEngine(settings, room, port: 0);
            var cheat = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.CheatDetected += reason => cheat.TrySetResult(reason);
            host.StartServer();
            for (int i = 0; i < 3; i++)
            {
                clients.Add(await PeerClient.ConnectAsync("127.0.0.1", "C" + i, "test", host.Port));
            }

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (host.Lobby.Count < 4 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            var snapshot = new TaskCompletionSource<SnapshotMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            clients[0].SnapshotReceived += message => snapshot.TrySetResult(message);
            host.StartMatch(1);
            SnapshotMessage message = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await clients[0].SendCommandAsync("pass", message.Revision, snapshotHash: "not-the-host-hash");
            string reason = await cheat.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(host.MatchCancelled);
            Assert.True(clients[0].LastError is not null || reason.Length > 0);
        }
        finally
        {
            foreach (PeerClient client in clients)
            {
                await client.DisposeAsync();
            }
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
