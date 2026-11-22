using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Tiisu.Net;

/// <summary>
/// Minecraft 风格的局域网自动查询：
/// <list type="bullet">
/// <item>房主在 UDP 17700 监听查询，收到 <see cref="NetworkConstants.DiscoveryMagic"/> 就回房间 JSON；</item>
/// <item>客户端向广播地址/回环地址发同一查询，收集超时内的响应；</item>
/// <item>房主每秒再广播一次房间信标，不依赖客户端先发包。</item>
/// </list>
/// 查询只暴露公开房间信息，不包含牌山、手牌或对局状态。
/// </summary>
public sealed class LanDiscovery : IDisposable
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private bool _disposed;

    /// <summary>启动房主查询监听。端口被占用时抛出 <see cref="SocketException"/>。</summary>
    public LanAdvertiser Advertise(RoomInfo initial, Func<RoomInfo>? refresh = null, int port = NetworkConstants.DefaultPort)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new LanAdvertiser(initial, refresh, port, _json);
    }

    /// <summary>
    /// 查询局域网内所有房间。会同时查询广播地址和 127.0.0.1，
    /// 因此单机双开/测试也能发现房主。
    /// </summary>
    public async Task<IReadOnlyList<RoomInfo>> SearchAsync(
        TimeSpan? timeout = null,
        int port = NetworkConstants.DefaultPort,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TimeSpan wait = timeout ?? TimeSpan.FromMilliseconds(900);
        var found = new ConcurrentDictionary<string, RoomInfo>(StringComparer.Ordinal);
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        byte[] query = Encoding.UTF8.GetBytes(NetworkConstants.DiscoveryMagic);
        foreach (IPAddress address in QueryAddresses())
        {
            try
            {
                await udp.SendAsync(query, new IPEndPoint(address, port), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // 单个网卡/广播地址失败不影响其它地址。
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(wait);
        try
        {
            while (!timeoutCts.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try
                {
                    packet = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (packet.Buffer.Length is 0 or > 4096)
                {
                    continue;
                }

                RoomInfo? room = TryParseRoom(packet.Buffer, out string? protocol);
                if (room is null || protocol != NetworkConstants.Protocol)
                {
                    continue;
                }

                string key = $"{room.RoomName}:{room.Port}:{room.HostName}";
                found[key] = room with { HostAddress = packet.RemoteEndPoint.Address.ToString() };
            }
        }
        catch (SocketException)
        {
            // 网络切换/无网卡时返回已收集结果。
        }

        return found.Values
            .OrderByDescending(r => r.IsJoinable)
            .ThenBy(r => r.RoomName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>广播地址：定向子网广播 + 回环。255.255.255.255 在部分路由器上被丢弃，所以两者都发。</summary>
    public static IReadOnlyList<IPAddress> QueryAddresses()
    {
        var result = new List<IPAddress> { IPAddress.Loopback, IPAddress.Broadcast };
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                    || nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork
                        || info.IPv4Mask is null)
                    {
                        continue;
                    }

                    byte[] ip = info.Address.GetAddressBytes();
                    byte[] mask = info.IPv4Mask.GetAddressBytes();
                    if (ip.Length != 4 || mask.Length != 4)
                    {
                        continue;
                    }

                    byte[] broadcast = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    }

                    result.Add(new IPAddress(broadcast));
                }
            }
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            // 取不到网卡信息时仍用回环/广播。
        }

        return result.Distinct().ToArray();
    }

    private RoomInfo? TryParseRoom(byte[] buffer, out string? protocol)
    {
        protocol = null;
        try
        {
            using var doc = JsonDocument.Parse(buffer);
            var root = doc.RootElement;
            protocol = root.TryGetProperty("protocol", out var p) ? p.GetString() : null;
            if (!root.TryGetProperty("room", out var room))
            {
                return null;
            }

            return room.Deserialize<RoomInfo>(_json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => _disposed = true;

    /// <summary>房主端监听/信标。线程在后台运行，Dispose 后退出。</summary>
    public sealed class LanAdvertiser : IDisposable
    {
        private readonly UdpClient _udp;
        private readonly RoomInfo _initial;
        private readonly Func<RoomInfo>? _refresh;
        private readonly JsonSerializerOptions _json;
        private readonly int _port;
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _listenThread;
        private readonly Thread _beaconThread;

        internal LanAdvertiser(RoomInfo initial, Func<RoomInfo>? refresh, int port, JsonSerializerOptions json)
        {
            _initial = initial;
            _refresh = refresh;
            _json = json;
            _port = port;
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.EnableBroadcast = true;
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            _listenThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "Tiisu.LanDiscovery.Listen",
            };
            _beaconThread = new Thread(BeaconLoop)
            {
                IsBackground = true,
                Name = "Tiisu.LanDiscovery.Beacon",
            };
            _listenThread.Start();
            _beaconThread.Start();
        }

        public RoomInfo Current => _refresh?.Invoke() ?? _initial;

        private void ListenLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    IPEndPoint remote = new(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref remote);
                    if (IsQuery(data))
                    {
                        SendRoom(remote);
                    }
                }
                catch (SocketException)
                {
                    if (_cts.IsCancellationRequested)
                    {
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void BeaconLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (_cts.Token.WaitHandle.WaitOne(1000))
                    {
                        return;
                    }

                    byte[] payload = BuildPayload();
                    foreach (IPAddress address in QueryAddresses())
                    {
                        try
                        {
                            _udp.Send(payload, payload.Length, new IPEndPoint(address, _port));
                        }
                        catch (SocketException)
                        {
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void SendRoom(IPEndPoint remote)
        {
            try
            {
                byte[] payload = BuildPayload();
                _udp.Send(payload, payload.Length, remote);
            }
            catch (SocketException)
            {
            }
        }

        private byte[] BuildPayload()
        {
            var envelope = new { protocol = NetworkConstants.Protocol, room = Current };
            return JsonSerializer.SerializeToUtf8Bytes(envelope, _json);
        }

        private static bool IsQuery(byte[] data)
        {
            if (data.Length is 0 or > 256)
            {
                return false;
            }

            string text = Encoding.UTF8.GetString(data).Trim();
            return string.Equals(text, NetworkConstants.DiscoveryMagic, StringComparison.Ordinal);
        }

        public void Dispose()
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            _cts.Cancel();
            _udp.Dispose();
            _listenThread.Join(500);
            _beaconThread.Join(500);
            _cts.Dispose();
        }
    }
}
