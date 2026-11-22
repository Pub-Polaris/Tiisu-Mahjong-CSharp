namespace Tiisu.Net;

/// <summary>联机协议常量。默认 TCP/UDP 共用 17700。</summary>
public static class NetworkConstants
{
    public const int DefaultPort = 17700;
    public const string Protocol = "tiisu-peer/1";
    public const string DiscoveryMagic = "TIISU_DISCOVER/1";
    public const int MaxPlayers = 4;
    public const int MaxNameLength = 24;
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
    string HostAddress = "")
{
    public static RoomInfo Create(string roomName, string hostName, int port, string version,
        int players = 1, string state = "lobby") =>
        new(
            Clean(roomName, 32, "七子房间"),
            Clean(hostName, NetworkConstants.MaxNameLength, "Host"),
            port,
            Math.Clamp(players, 0, NetworkConstants.MaxPlayers),
            NetworkConstants.MaxPlayers,
            Clean(state, 16, "lobby"),
            Clean(version, 32, "0"));

    public bool IsJoinable =>
        string.Equals(State, "lobby", StringComparison.OrdinalIgnoreCase)
        && Players < MaxPlayers;

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
