using System.Net;
using System.Net.Sockets;
using Tiisu.Net;

namespace Tiisu.Net.Tests;

public sealed class LanDiscoveryTests
{
    [Fact]
    public async Task Discovery_finds_advertised_room_on_loopback()
    {
        int port = FreeUdpPort();
        using var discovery = new LanDiscovery();
        var room = RoomInfo.Create("测试房", "Host", port, "test", 1);
        using var advertiser = discovery.Advertise(room, port: port);

        var results = await discovery.SearchAsync(TimeSpan.FromMilliseconds(700), port);

        var found = Assert.Single(results);
        Assert.Equal("测试房", found.RoomName);
        Assert.Equal("Host", found.HostName);
        Assert.Equal(port, found.Port);
        Assert.True(found.IsJoinable);
    }

    [Fact]
    public void RoomInfo_cleans_and_clamps_public_fields()
    {
        var room = RoomInfo.Create(new string('x', 80), "", 17700, "v", players: 99, state: "playing");

        Assert.Equal(32, room.RoomName.Length);
        Assert.Equal("Host", room.HostName);
        Assert.Equal(NetworkConstants.MaxPlayers, room.Players);
        Assert.False(room.IsJoinable);
    }

    private static int FreeUdpPort()
    {
        using var socket = new UdpClient(AddressFamily.InterNetwork);
        socket.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}

public sealed class PeerServerTests
{
    [Fact]
    public async Task Handshake_assigns_seat_and_receives_targeted_snapshot()
    {
        int port = FreeTcpPort();
        var room = RoomInfo.Create("联机测试", "Host", port, "test");
        await using var server = new PeerServer(room, port);
        server.Start();
        var received = new TaskCompletionSource<SnapshotMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = await PeerClient.ConnectAsync("127.0.0.1", "Client", "test", port);
        client.SnapshotReceived += snapshot => received.TrySetResult(snapshot);

        Assert.Equal(1, client.Seat);
        Assert.Equal("联机测试", client.Room!.RoomName);
        Assert.Contains(client.Players, p => p.Seat == 0 && p.Name == "Host");
        Assert.Contains(client.Players, p => p.Seat == 1 && p.Name == "Client");

        await server.SendSnapshotAsync(1, 7, "{\"phase\":\"human_discard\"}");
        var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(7, snapshot.Revision);
        Assert.Equal(1, snapshot.Seat);
        Assert.Contains("human_discard", snapshot.Snapshot);
    }

    [Fact]
    public async Task Invalid_command_cancels_the_room()
    {
        int port = FreeTcpPort();
        var room = RoomInfo.Create("作弊测试", "Host", port, "test");
        await using var server = new PeerServer(room, port);
        server.ValidateCommand = _ => false;
        var cheat = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CheatDetected += reason => cheat.TrySetResult(reason);
        server.Start();

        await using var client = await PeerClient.ConnectAsync("127.0.0.1", "Client", "test", port);
        var clientCheat = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.CheatDetected += reason => clientCheat.TrySetResult(reason);

        await client.SendCommandAsync("discard", 1, index: 0);

        string serverReason = await cheat.Task.WaitAsync(TimeSpan.FromSeconds(3));
        string clientReason = await clientCheat.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("inconsistent", serverReason);
        Assert.Equal(serverReason, clientReason);
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
