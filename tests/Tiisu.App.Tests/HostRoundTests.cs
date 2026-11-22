using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Tiisu.Engine.Random;
using Tiisu.Net;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class HostRoundTests
{
    [Fact]
    public async Task Four_network_players_can_finish_one_round()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var clients = new List<PeerClient>();
        try
        {
            var room = RoomInfo.Create("整局测试", "Host", 0, "test");
            using var host = new HostEngine(new Settings(), room, port: 0, random: new FixedRandom());
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
            var roundEnds = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            clients[0].RoundEndJson += json => roundEnds.TrySetResult(json);
            for (int i = 0; i < clients.Count; i++)
            {
                int index = i;
                clients[i].SnapshotReceived += message => Volatile.Write(ref latest[index], message);
            }

            host.StartMatch(1);
            var timeout = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < timeout && host.Phase is not (MatchPhase.RoundEnded or MatchPhase.MatchOver))
            {
                if (cheat.Task.IsCompleted)
                {
                    Assert.Fail("unexpected cheat detection");
                }

                bool sent = false;
                HumanAction hostActions = host.LocalSession.LatestSnapshot.Actions;
                if ((hostActions & HumanAction.Discard) != 0)
                {
                    host.Post(GameCommand.Discard(0));
                    sent = true;
                }
                else if ((hostActions & HumanAction.Pass) != 0)
                {
                    host.Post(GameCommand.Pass());
                    sent = true;
                }

                if (!sent)
                {
                    for (int i = 0; i < clients.Count && !sent; i++)
                    {
                        SnapshotMessage? message = Volatile.Read(ref latest[i]);
                        if (message is null)
                        {
                            continue;
                        }

                        using var document = JsonDocument.Parse(message.Snapshot);
                        string phase = document.RootElement.GetProperty("phase").GetString() ?? string.Empty;
                        JsonElement own = document.RootElement.GetProperty("seats")[message.Seat];
                        int handCount = own.GetProperty("hand").GetArrayLength();
                        int actions = document.RootElement.TryGetProperty("actions", out var a) ? a.GetInt32() : 0;
                        var flags = (HumanAction)actions;
                        string? kind = null;
                        if (phase == "human_discard" && handCount > 0 && (flags & HumanAction.Discard) != 0)
                        {
                            kind = "discard";
                        }
                        else if ((phase == "human_ron" || phase == "human_meld") && (flags & HumanAction.Pass) != 0)
                        {
                            kind = "pass";
                        }

                        if (kind is null)
                        {
                            continue;
                        }

                        await clients[i].SendCommandAsync(kind, message.Revision, index: 0,
                            snapshotHash: Hash(message.Snapshot));
                        sent = true;
                    }
                }

                await Task.Delay(sent ? 10 : 30);
            }

            Assert.True(host.Phase is MatchPhase.RoundEnded or MatchPhase.MatchOver,
                $"round did not finish; phase={host.Phase}, revision={host.LatestSnapshot.Revision}, error={host.LastError}");
            string roundEndJson = await roundEnds.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains("\"kind\"", roundEndJson);
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
