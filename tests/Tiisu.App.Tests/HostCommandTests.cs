using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tiisu.App.Runtime;
using Tiisu.Engine.Random;
using Tiisu.Net;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class HostCommandTests
{
    [Fact]
    public async Task Host_accepts_a_valid_remote_discard()
    {
        var settings = new Settings();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var clients = new List<PeerClient>();
        try
        {
            var room = RoomInfo.Create("指令房", "Host", 0, "test");
            using var host = new HostEngine(settings, room, port: 0, random: new FixedRandom());
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

            var latest = new SnapshotMessage?[clients.Count];
            for (int i = 0; i < clients.Count; i++)
            {
                int index = i;
                clients[i].SnapshotReceived += message => Volatile.Write(ref latest[index], message);
            }

            host.StartMatch(1);
            var started = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < started && latest.All(s => s is null))
            {
                await Task.Delay(20);
            }

            Assert.All(latest, Assert.NotNull);
            Console.WriteLine($"host start phase={host.Phase} actions={host.LatestSnapshot.Actions} rev={host.LatestSnapshot.Revision}");

            // Host 本地是 Seat 0；若首巡是 Host，先让 Host 打一张，把行动权交出去。
            if (host.LocalSession.LatestSnapshot.Actions.HasFlag(Tiisu.App.Table.HumanAction.Discard))
            {
                host.Post(GameCommand.Discard(0));
            }

            string? sentSeat = null;
            string debug = string.Empty;
            for (int attempt = 0; attempt < 40 && sentSeat is null; attempt++)
            {
                await Task.Delay(50);
                var current = new List<string>();
                for (int i = 0; i < clients.Count; i++)
                {
                    SnapshotMessage? message = Volatile.Read(ref latest[i]);
                    if (message is null)
                    {
                        continue;
                    }

                    using var document = JsonDocument.Parse(message.Snapshot);
                    string phase = document.RootElement.GetProperty("phase").GetString() ?? "?";
                    JsonElement seats = document.RootElement.GetProperty("seats");
                    JsonElement own = seats[message.Seat];
                    int handCount = own.GetProperty("hand").GetArrayLength();
                    bool human = own.GetProperty("isHuman").GetBoolean();
                    int actions = document.RootElement.TryGetProperty("actions", out var a)
                        ? a.GetInt32()
                        : 0;
                    current.Add($"seat{message.Seat}:{phase}:human={human}:hand={handCount}:actions={actions}");
                    if (!human || handCount == 0)
                    {
                        continue;
                    }

                    string? command = null;
                    if (phase == "human_discard")
                    {
                        command = "discard";
                    }
                    else if (phase == "human_meld" && (actions & (int)Tiisu.App.Table.HumanAction.Pass) != 0)
                    {
                        command = "pass";
                    }

                    if (command is null)
                    {
                        continue;
                    }

                    string hash = Hash(message.Snapshot);
                    long before = host.LatestSnapshot.Revision;
                    await clients[i].SendCommandAsync(command, message.Revision, index: 0, snapshotHash: hash);
                    await Task.Delay(300);
                    Assert.True(!host.MatchCancelled, host.LastError ?? "cancelled");
                    Assert.True(host.LatestSnapshot.Revision > before || host.Phase != MatchPhase.HumanDiscard);
                    if (command == "discard")
                    {
                        sentSeat = "ok";
                    }

                    break;
                }

                debug = string.Join(" | ", current);
            }

            Assert.True(sentSeat is not null, debug + " / host=" + host.Phase);
            Assert.False(cheat.Task.IsCompleted);
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

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class FixedRandom : IRandomSource
    {
        public double NextDouble() => 0.25;
    }
}
