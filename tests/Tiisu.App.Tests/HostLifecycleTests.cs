using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Tiisu.App.Runtime;
using Tiisu.Net;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class HostLifecycleTests
{
    [Fact]
    public async Task Stop_disconnects_clients_and_releases_port()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        int port = FreeTcpAndUdpPort();
        var room = RoomInfo.Create("生命周期", "Host", port, "test");
        var clients = new List<PeerClient>();
        try
        {
            var host = new HostEngine(new Settings(), room, port);
            host.StartServer();
            for (int i = 0; i < 3; i++)
            {
                clients.Add(await PeerClient.ConnectAsync("127.0.0.1", "C" + i, "test", port));
            }

            var disconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            clients[0].Disconnected += reason => disconnected.TrySetResult(reason);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (host.Lobby.Count < 4 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            await host.StopAsync();
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // Rebind both TCP and UDP on the same port immediately.
            var second = new HostEngine(new Settings(), room, port);
            second.StartServer();
            await second.StopAsync();

            // First host must be idempotent.
            await host.StopAsync();
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

    private static int FreeTcpAndUdpPort()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
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
