namespace Tiisu.Net;

/// <summary>联机协议常量。默认 TCP/UDP 共用 17700。</summary>
public static class NetworkConstants
{
    public const int DefaultPort = 17700;
    public const string Protocol = "tiisu-peer/1";
    public const string DiscoveryMagic = "TIISU_DISCOVER/1";
    public const int MaxPlayers = 4;
    public const int MaxNameLength = 24;
    public const int MaxChatLength = 120;
    public const int MaxMessageBytes = 1024 * 1024;
}

/// <summary>局域网房间摘要（自动查询/大厅共用的公开信息）。</summary>
public sealed record RoomInfo(
    string RoomName,
    string HostName,
    int Port,
    int Players,
    int MaxPlayers,
    string State,
    string Version,
    string HostAddress = "",
    string Transport = "tcp",
    string Zone = "A",
    int Table = 1,
    int Mode = 1,
    bool AiFill = true)
{
    public static readonly string[] Zones = { "A", "B", "C", "D" };
    public const int TablesPerZone = 2;
    public const int MaxTables = 8;

    public static RoomInfo Create(string roomName, string hostName, int port, string version,
        int players = 1, string state = "lobby", string? transport = null,
        string? zone = null, int table = 1, int mode = 1, bool aiFill = true) =>
        new(
            Clean(roomName, 32, "七子房间"),
            Clean(hostName, NetworkConstants.MaxNameLength, "Host"),
            port,
            Math.Clamp(players, 0, NetworkConstants.MaxPlayers),
            NetworkConstants.MaxPlayers,
            Clean(state, 16, "lobby"),
            Clean(version, 32, "0"),
            string.Empty,
            NormalizeTransport(transport),
            NormalizeZone(zone),
            Math.Clamp(table, 1, TablesPerZone),
            Math.Clamp(mode, 1, 16),
            aiFill);

    public bool IsJoinable =>
        string.Equals(State, "lobby", StringComparison.OrdinalIgnoreCase)
        && Players < MaxPlayers;

    public string SlotLabel => NormalizeZone(Zone) + Table;

    public static string NormalizeZone(string? zone)
    {
        string value = (zone ?? string.Empty).Trim().ToUpperInvariant();
        return Zones.Contains(value, StringComparer.Ordinal) ? value : "A";
    }

    public static string NormalizeTransport(string? transport)
    {
        return string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase)
            ? "websocket"
            : "tcp";
    }

    public static bool IsSupportedTransport(string? transport) =>
        string.Equals(transport, "tcp", StringComparison.OrdinalIgnoreCase)
        || string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase);

    public static string TransportLabel(string transport) =>
        string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "WS" : "TCP";

    public static int? FirstFreeTable(string? zone, IEnumerable<RoomInfo> rooms)
    {
        string normalized = NormalizeZone(zone);
        var occupied = rooms
            .Where(r => string.Equals(NormalizeZone(r.Zone), normalized, StringComparison.Ordinal))
            .Select(r => Math.Clamp(r.Table, 1, TablesPerZone))
            .ToHashSet();
        for (int table = 1; table <= TablesPerZone; table++)
        {
            if (!occupied.Contains(table))
            {
                return table;
            }
        }

        return null;
    }

    private static string Clean(string? value, int max, string fallback)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return fallback;
        }

        return text.Length <= max ? text : text[..max];
    }
}

/// <summary>握手后服务端分配给客户端的信息。</summary>
public sealed record WelcomeInfo(int Seat, RoomInfo Room, string Protocol);
