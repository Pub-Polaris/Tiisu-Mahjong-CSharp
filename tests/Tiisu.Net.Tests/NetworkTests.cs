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

    [Fact]
    public async Task Advertiser_dispose_releases_udp_port()
    {
        int port = FreeUdpPort();
        using var discovery = new LanDiscovery();
        RoomInfo room = RoomInfo.Create("旧房", "Host", port, "test");
        using (var first = discovery.Advertise(room, port: port))
        {
        }

        using (var second = discovery.Advertise(room with { RoomName = "新房" }, port: port))
        {
        }

        Assert.True(true);
    }

    [Fact]
    public void RoomInfo_defaults_to_A1_TCP_and_allocates_first_free_table()
    {
        RoomInfo room = RoomInfo.Create("房间", "Host", 17700, "test");
        Assert.Equal("A", room.Zone);
        Assert.Equal(1, room.Table);
        Assert.Equal("tcp", room.Transport);
        Assert.Equal("A1", room.SlotLabel);

        var rooms = new[]
        {
            room,
            RoomInfo.Create("房间2", "Host2", 17700, "test", zone: "A", table: 1),
            RoomInfo.Create("房间3", "Host3", 17700, "test", zone: "B", table: 1),
        };
        Assert.Equal(2, RoomInfo.FirstFreeTable("A", rooms));
        Assert.Equal(2, RoomInfo.FirstFreeTable("B", rooms));
        Assert.Null(RoomInfo.FirstFreeTable("A",
            new[] { rooms[0], rooms[0] with { Table = 2 } }));
    }

    [Fact]
    public void RoomInfo_old_json_without_new_fields_uses_defaults()
    {
        const string json = "{\"roomName\":\"旧房间\",\"hostName\":\"Host\",\"port\":17700,"
            + "\"players\":1,\"maxPlayers\":4,\"state\":\"lobby\",\"version\":\"v\",\"hostAddress\":\"127.0.0.1\"}";
        RoomInfo? room = System.Text.Json.JsonSerializer.Deserialize<RoomInfo>(
            json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(room);
        Assert.Equal("A", room!.Zone);
        Assert.Equal(1, room.Table);
        Assert.Equal("tcp", room.Transport);
        Assert.Equal(1, room.Mode);
        Assert.True(room.AiFill);
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

    [Fact]
    public async Task WebSocket_handshake_assigns_seat_and_receives_snapshot()
    {
        int port = FreeTcpAndUdpPort();
        var room = RoomInfo.Create("WS测试", "Host", port, "test", transport: "websocket", zone: "B", table: 2);
        await using var server = new PeerServer(room, port, "websocket");
        server.Start();
        var received = new TaskCompletionSource<SnapshotMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = await PeerClient.ConnectAsync(
            "127.0.0.1", "Client", "test", port, "websocket");
        client.SnapshotReceived += snapshot => received.TrySetResult(snapshot);

        Assert.Equal(1, client.Seat);
        Assert.Equal("websocket", client.Room!.Transport);
        Assert.Equal("B2", client.Room.SlotLabel);

        await server.SendSnapshotAsync(1, 8, "{\"phase\":\"human_discard\"}");
        var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(8, snapshot.Revision);
        Assert.Equal(1, snapshot.Seat);
    }

    [Fact]
    public async Task WebSocket_rejects_plain_http_request()
    {
        int port = FreeTcpAndUdpPort();
        var room = RoomInfo.Create("WS拒绝", "Host", port, "test", transport: "websocket");
        await using var server = new PeerServer(room, port, "websocket");
        server.Start();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        using var stream = tcp.GetStream();
        byte[] request = System.Text.Encoding.ASCII.GetBytes(
            "GET /tiisu HTTP/1.1\r\nHost: localhost\r\n\r\n");
        await stream.WriteAsync(request);
        byte[] buffer = new byte[256];
        int read = await stream.ReadAsync(buffer);
        string response = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
        Assert.True(response.Contains("400", StringComparison.Ordinal)
            || response.Contains("426", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stop_releases_tcp_and_udp_and_allows_restart()
    {
        int port = FreeTcpAndUdpPort();
        var room = RoomInfo.Create("释放测试", "Host", port, "test");
        await using (var first = new PeerServer(room, port))
        {
            first.Start();
            Assert.Equal(port, first.Port);
            await first.StopAsync();
        }

        await using (var second = new PeerServer(room, port))
        {
            second.Start();
            Assert.Equal(port, second.Port);
            await second.StopAsync();
        }
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int FreeTcpAndUdpPort()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            int port = FreeTcpPort();
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                return port;
            }
            catch (SocketException)
            {
            }
        }

        throw new InvalidOperationException("no free TCP+UDP port");
    }
}
