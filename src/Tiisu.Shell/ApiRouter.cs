using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Tiisu.Api;

namespace Tiisu.Shell;

/// <summary>
/// <c>https://tiisu.local/api/*</c> 的直接应答（不启 Kestrel、不启 Python）。
/// 路由与响应形状逐项照 <c>NET/src/Tiisu.Api/TiisuApiApp.cs</c>（以及 Web/server.py）。
/// 数据根目录由调用方指定（Shell 传 <c>%APPDATA%\TiisuMahjong</c>），
/// settings.json / state.json / stats.json / logs 都落在那里。
/// </summary>
internal sealed class ApiRouter
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string TextContentType = "text/plain; charset=utf-8";
    private const string NdjsonContentType = "application/x-ndjson; charset=utf-8";

    private readonly DataStore _store;

    public ApiRouter(string dataDirectory) => _store = new DataStore(dataDirectory);

    public void Handle(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs args, Action<string> log)
    {
        var uri = new Uri(args.Request.Uri);
        string path = uri.AbsolutePath;
        string method = (args.Request.Method ?? "GET").ToUpperInvariant();
        var query = ParseQuery(uri.Query);
        string body = ReadBody(args.Request, log);

        try
        {
            var (status, contentType, text) = Route(method, path, query, body);
            log($"[api] {method} {path} -> {status} ({Encoding.UTF8.GetByteCount(text)} bytes)");
            args.Response = CreateResponse(core, status, contentType, text);
        }
        catch (Exception ex)
        {
            log($"{method} {path} 失败: {ex.GetType().Name}: {ex.Message}");
            args.Response = CreateResponse(core, 500, TextContentType, "internal error");
        }
    }

    private (int Status, string ContentType, string Body) Route(
        string method,
        string path,
        IReadOnlyDictionary<string, string> query,
        string body)
    {
        if (method == "OPTIONS")
        {
            return (200, TextContentType, string.Empty);
        }

        switch (path)
        {
            case "/api/settings":
                if (method == "GET")
                {
                    return Json(_store.LoadSettings());
                }

                if (method == "POST")
                {
                    var node = ParseJson(body);
                    return Json(DataStore.IsTruthy(node) ? _store.SaveSettings(node!) : _store.LoadSettings());
                }

                break;

            case "/api/state":
                if (method == "GET")
                {
                    return Json(_store.ReadNode(_store.StatePath) ?? new JsonObject());
                }

                if (method == "POST")
                {
                    var node = ParseJson(body);
                    if (!DataStore.IsTruthy(node))
                    {
                        return Json(_store.ReadNode(_store.StatePath) ?? new JsonObject());
                    }

                    _store.WriteJson(_store.StatePath, node);
                    return Json(node);
                }

                break;

            case "/api/stats":
                if (method == "GET")
                {
                    return Json(_store.LoadStats());
                }

                if (method == "POST")
                {
                    return Json(_store.PostStats(ParseJson(body)));
                }

                break;

            case "/api/log":
                if (method == "POST")
                {
                    var node = ParseJson(body) as JsonObject;
                    _store.AppendLogMessage(node is null ? string.Empty : DataStore.GetString(node, "message"));
                    return (200, TextContentType, "ok");
                }

                break;

            case "/api/chrlog":
                if (method == "POST")
                {
                    if (ParseJson(body) is JsonObject obj)
                    {
                        _store.AppendChrLog(
                            DataStore.GetString(obj, "session"),
                            DataStore.GetString(obj, "entry"));
                    }

                    return (200, TextContentType, "ok");
                }

                break;

            case "/api/round_end":
                if (method == "POST")
                {
                    string session = ParseJson(body) is JsonObject obj ? DataStore.GetString(obj, "session") : string.Empty;
                    string? archived = _store.ArchiveSession(session);
                    return (200, TextContentType, "archived:" + (archived ?? "none"));
                }

                break;

            case "/api/roundlog":
                if (method == "POST")
                {
                    AppendRoundLog(body);
                    return (200, TextContentType, "ok");
                }

                if (method == "GET")
                {
                    return (200, NdjsonContentType, QueryRoundLogs(query));
                }

                break;
        }

        return (404, TextContentType, string.Empty);
    }

    private void AppendRoundLog(string body)
    {
        if (ParseJson(body) is not JsonObject obj)
        {
            return;
        }

        string session = DataStore.GetString(obj, "session");
        int round = (int)DataStore.ToInt(obj["round"]);
        int player = (int)DataStore.ToInt(obj["player"]);
        string playerUuid = DataStore.GetString(obj, "playerUUID");
        if (playerUuid.Length == 0)
        {
            playerUuid = DataStore.GetString(obj, "uuid");
        }

        if (obj["events"] is JsonArray events)
        {
            _store.AppendRoundLog(session, round, player, playerUuid, events);
        }
    }

    private string QueryRoundLogs(IReadOnlyDictionary<string, string> query)
    {
        string session = query.TryGetValue("session", out var s) ? s : string.Empty;

        int? round = null;
        if (query.TryGetValue("round", out var roundText) && !string.IsNullOrWhiteSpace(roundText))
        {
            if (!int.TryParse(roundText, out int parsedRound))
            {
                return string.Empty;
            }

            round = parsedRound;
        }

        int? player = null;
        if (query.TryGetValue("player", out var playerText) && !string.IsNullOrWhiteSpace(playerText))
        {
            if (!DataStore.TryParsePlayerFilter(playerText, out int parsedPlayer))
            {
                return string.Empty;
            }

            player = parsedPlayer;
        }

        var lines = _store.QueryRoundLogs(
            string.IsNullOrWhiteSpace(session) ? null : session,
            round,
            player);

        return lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
    }

    private static (int Status, string ContentType, string Body) Json(JsonNode? node)
        => (200, JsonContentType, DataStore.Serialize(node));

    private static CoreWebView2WebResourceResponse CreateResponse(
        CoreWebView2 core,
        int status,
        string contentType,
        string body)
    {
        string headers = "Content-Type: " + contentType + "\r\n"
                         + "Access-Control-Allow-Origin: *\r\n"
                         + "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n"
                         + "Access-Control-Allow-Headers: Content-Type\r\n"
                         + "Cache-Control: no-store\r\n";

        return core.Environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes(body), writable: false),
            status,
            status == 200 ? "OK" : status == 404 ? "Not Found" : "Error",
            headers);
    }

    private static string ReadBody(CoreWebView2WebResourceRequest request, Action<string> log)
    {
        try
        {
            var content = request.Content;
            if (content is null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(content, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            log("读取请求体失败: " + ex.Message);
            return string.Empty;
        }
    }

    private static JsonNode? ParseJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = part.IndexOf('=');
            string key = separator >= 0 ? part[..separator] : part;
            string value = separator >= 0 ? part[(separator + 1)..] : string.Empty;
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }
}
