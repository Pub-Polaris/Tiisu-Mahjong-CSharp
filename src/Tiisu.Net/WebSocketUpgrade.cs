using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Tiisu.Net;

/// <summary>
/// 在现有 TcpListener 上完成 RFC6455 握手，避免 HttpListener 的 URLACL/管理员限制。
/// </summary>
internal static class WebSocketUpgrade
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int MaxHeaderBytes = 8 * 1024;

    public static async Task<WebSocket?> AcceptAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        client.NoDelay = true;
        NetworkStream stream = client.GetStream();
        string? headers = await ReadHeadersAsync(stream, cancellationToken).ConfigureAwait(false);
        if (headers is null)
        {
            return null;
        }

        var lines = headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("GET ", StringComparison.Ordinal))
        {
            await RejectAsync(stream, "400 Bad Request", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon > 0)
            {
                map[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }
        }

        if (!map.TryGetValue("Upgrade", out string? upgrade)
            || !upgrade.Contains("websocket", StringComparison.OrdinalIgnoreCase)
            || !map.TryGetValue("Connection", out string? connection)
            || !connection.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
            || !map.TryGetValue("Sec-WebSocket-Version", out string? version)
            || version.Trim() != "13"
            || !map.TryGetValue("Sec-WebSocket-Key", out string? key)
            || string.IsNullOrWhiteSpace(key))
        {
            await RejectAsync(stream, "426 Upgrade Required", cancellationToken).ConfigureAwait(false);
            return null;
        }

        string accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid)));
        string subProtocol = map.TryGetValue("Sec-WebSocket-Protocol", out string? requested)
            ? requested.Split(',')[0].Trim()
            : string.Empty;
        string response =
            "HTTP/1.1 101 Switching Protocols\r\n"
            + "Upgrade: websocket\r\n"
            + "Connection: Upgrade\r\n"
            + "Sec-WebSocket-Accept: " + accept + "\r\n"
            + (subProtocol.Length > 0 ? "Sec-WebSocket-Protocol: " + subProtocol + "\r\n" : string.Empty)
            + "\r\n";
        byte[] responseBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
        return WebSocket.CreateFromStream(
            stream,
            isServer: true,
            subProtocol: subProtocol.Length > 0 ? subProtocol : null,
            keepAliveInterval: TimeSpan.FromSeconds(30));
    }

    private static async Task<string?> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(512);
        byte[] one = new byte[1];
        while (bytes.Count < MaxHeaderBytes)
        {
            int read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            bytes.Add(one[0]);
            int count = bytes.Count;
            if (count >= 4
                && bytes[count - 4] == '\r'
                && bytes[count - 3] == '\n'
                && bytes[count - 2] == '\r'
                && bytes[count - 1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        return null;
    }

    private static async Task RejectAsync(NetworkStream stream, string status, CancellationToken cancellationToken)
    {
        try
        {
            byte[] body = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " + status + "\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }
}
