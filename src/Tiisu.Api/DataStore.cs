using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tiisu.Api;

/// <summary>
/// Disk-backed store that mirrors Web/server.py.
///
/// Settings / state / stats stay plain JSON files in the same directory as the
/// Python server (Web by default). Round logs are JSONL, one event per line.
/// </summary>
public sealed class DataStore
{
    private const string DefaultSettingsJson = """
    {
      "thinkSeconds": 30,
      "optionalYaku": [],
      "showMa": false,
      "showDebug": false,
      "showWallViewer": false,
      "recordTiles": false
    }
    """;

    private const string DefaultStatsJson = """
    {
      "tiisuinCount": 0,
      "witnessCount": 0,
      "wins": 0,
      "rounds": 0,
      "byPlayer": {}
    }
    """;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex UnsafePathChars = new("[\\\\/:*?\"<>|]", RegexOptions.Compiled);
    private static readonly Regex RoundDirPattern = new("^round_(\\d{3})_p(\\d+)_", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        // match Python json.dumps(..., ensure_ascii=False): keep Chinese unescaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null
    };

    private static readonly JsonSerializerOptions IndentedJson = new(CompactJson)
    {
        WriteIndented = true
    };

    private readonly object _appendLock = new();

    public DataStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        System.IO.Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");

    public string StatePath => Path.Combine(DirectoryPath, "state.json");

    public string StatsPath => Path.Combine(DirectoryPath, "stats.json");

    public string LogsDirectory => Path.Combine(DirectoryPath, "logs");

    public static string Serialize(JsonNode? node, bool indented = false)
        => node?.ToJsonString(indented ? IndentedJson : CompactJson) ?? "null";

    public static JsonObject DefaultSettings()
        => (JsonObject)JsonNode.Parse(DefaultSettingsJson)!;

    public static JsonObject DefaultStats()
        => (JsonObject)JsonNode.Parse(DefaultStatsJson)!;

    public JsonObject ReadObject(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject
                ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    public JsonNode? ReadNode(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    public void WriteJson(string path, JsonNode? node)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lock (_appendLock)
        {
            File.WriteAllText(path, Serialize(node, indented: true), Utf8NoBom);
        }
    }

    public static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value?.DeepClone();
        }
    }

    public JsonObject LoadSettings()
    {
        var settings = DefaultSettings();
        Merge(settings, ReadObject(SettingsPath));
        return settings;
    }

    public JsonObject SaveSettings(JsonNode body)
    {
        var settings = LoadSettings();
        if (body is JsonObject obj)
        {
            Merge(settings, obj);
        }

        WriteJson(SettingsPath, settings);
        return settings;
    }

    public JsonObject LoadStats()
        => NormalizeStats(MergeCopy(DefaultStats(), ReadObject(StatsPath)));

    public JsonObject PostStats(JsonNode? body)
    {
        var stats = LoadStats();
        if (body is not JsonObject obj || !IsTruthy(body))
        {
            return stats;
        }

        if (GetString(obj, "kind") == "reset")
        {
            stats = DefaultStats();
        }
        else
        {
            stats["tiisuinCount"] = ToInt(stats["tiisuinCount"]) + ToInt(obj["tiisuin"]);
            var witnessCount = obj["witness"] is JsonArray witness ? witness.Count : 0;
            stats["witnessCount"] = ToInt(stats["witnessCount"]) + witnessCount;
            stats["wins"] = ToInt(stats["wins"]) + ToInt(obj["wins"]);
            stats["rounds"] = ToInt(stats["rounds"]) + ToInt(obj["rounds"]);

            var byPlayer = stats["byPlayer"] as JsonObject ?? new JsonObject();
            var playerNode = obj["player"];
            if (playerNode is not null && IsTruthy(playerNode))
            {
                var playerName = NodeToString(playerNode);
                var entry = byPlayer[playerName] as JsonObject ?? new JsonObject();
                entry["wins"] = ToInt(entry["wins"]) + ToInt(obj["wins"]);
                entry["tiisuin"] = ToInt(entry["tiisuin"]) + ToInt(obj["tiisuin"]);
                byPlayer[playerName] = entry;
            }

            if (obj["witness"] is JsonArray witnessNames)
            {
                foreach (var witnessNode in witnessNames)
                {
                    var witnessName = NodeToString(witnessNode);
                    if (witnessName.Length == 0)
                    {
                        continue;
                    }

                    var entry = byPlayer[witnessName] as JsonObject ?? new JsonObject();
                    entry["witness"] = ToInt(entry["witness"]) + 1;
                    byPlayer[witnessName] = entry;
                }
            }

            stats["byPlayer"] = byPlayer;
        }

        WriteJson(StatsPath, stats);
        return stats;
    }

    public JsonObject NormalizeStats(JsonObject stats)
    {
        if (stats.ContainsKey("daxingqiCount"))
        {
            stats["tiisuinCount"] = ToInt(stats["tiisuinCount"]) + ToInt(stats["daxingqiCount"]);
            stats.Remove("daxingqiCount");
        }

        if (stats["byPlayer"] is JsonObject byPlayer)
        {
            foreach (var pair in byPlayer.ToList())
            {
                if (pair.Value is not JsonObject entry || !entry.ContainsKey("daxingqi"))
                {
                    continue;
                }

                entry["tiisuin"] = ToInt(entry["tiisuin"]) + ToInt(entry["daxingqi"]);
                entry.Remove("daxingqi");
            }
        }

        return stats;
    }

    // ── legacy helpers, same semantics as server.py ─────────────────────

    public static long ToInt(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        if (value.TryGetValue<double>(out var d))
        {
            return (long)d;
        }

        if (value.TryGetValue<bool>(out var b))
        {
            return b ? 1 : 0;
        }

        if (value.TryGetValue<string>(out var s) && long.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    public static string GetString(JsonObject obj, string key)
        => NodeToString(obj[key]);

    public static string NodeToString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return string.Empty;
        }

        if (value.TryGetValue<string>(out var s))
        {
            return s;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (value.TryGetValue<double>(out var d))
        {
            return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    public static bool IsTruthy(JsonNode? node)
    {
        if (node is null)
        {
            return false;
        }

        if (node is JsonObject obj)
        {
            return obj.Count > 0;
        }

        if (node is JsonArray array)
        {
            return array.Count > 0;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var b))
            {
                return b;
            }

            if (value.TryGetValue<double>(out var d))
            {
                return d != 0;
            }

            if (value.TryGetValue<string>(out var s))
            {
                return s.Length > 0;
            }
        }

        return true;
    }

    // ── Logs ────────────────────────────────────────────────────────────

    public void AppendLogMessage(string message)
        => AppendLine(Path.Combine(LogsDirectory, "output.txt"), message);

    public void AppendChrLog(string session, string entry)
    {
        var safe = Sanitize(session);
        AppendLine(Path.Combine(LogsDirectory, safe + "_chr_console.log"), entry);
    }

    public void AppendRoundLog(
        string session,
        int round,
        int player,
        string playerUuid,
        IEnumerable<JsonNode?> events)
    {
        var path = RoundLogPath(session, round, player, playerUuid);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new StringBuilder();
        foreach (var node in events)
        {
            if (node is null)
            {
                continue;
            }

            builder.Append(Serialize(node)).Append('\n');
        }

        lock (_appendLock)
        {
            File.AppendAllText(path, builder.ToString(), Utf8NoBom);
        }
    }

    public string RoundLogPath(string session, int round, int player, string playerUuid)
    {
        var safeSession = Sanitize(session);
        var safeUuid = Sanitize(playerUuid);
        var folder = $"round_{round:000}_p{player}_{safeUuid}";
        return Path.Combine(LogsDirectory, safeSession, folder, "events.jsonl");
    }

    public IReadOnlyList<string> QueryRoundLogs(string? session, int? round, int? player)
    {
        var results = new List<string>();
        if (!System.IO.Directory.Exists(LogsDirectory))
        {
            return results;
        }

        IEnumerable<string> sessionDirectories;
        if (!string.IsNullOrWhiteSpace(session))
        {
            var one = Path.Combine(LogsDirectory, Sanitize(session));
            sessionDirectories = System.IO.Directory.Exists(one)
                ? new[] { one }
                : Array.Empty<string>();
        }
        else
        {
            sessionDirectories = System.IO.Directory.GetDirectories(LogsDirectory);
        }

        foreach (var sessionDirectory in sessionDirectories.OrderBy(x => x, StringComparer.Ordinal))
        {
            foreach (var roundDirectory in System.IO.Directory
                         .GetDirectories(sessionDirectory)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                var match = RoundDirPattern.Match(Path.GetFileName(roundDirectory));
                if (!match.Success)
                {
                    continue;
                }

                var dirRound = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var dirPlayer = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (round is not null && dirRound != round.Value)
                {
                    continue;
                }

                if (player is not null && dirPlayer != player.Value)
                {
                    continue;
                }

                var file = Path.Combine(roundDirectory, "events.jsonl");
                if (!File.Exists(file))
                {
                    continue;
                }

                foreach (var line in File.ReadLines(file, Encoding.UTF8))
                {
                    if (line.Length > 0)
                    {
                        results.Add(line);
                    }
                }
            }
        }

        return results;
    }

    public static bool TryParsePlayerFilter(string? text, out int player)
    {
        player = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (int.TryParse(value, out var numeric))
        {
            player = numeric;
            return true;
        }

        var normalized = value.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        player = normalized switch
        {
            "human" or "you" or "你" => 0,
            "ai_south" or "ai_南" or "ai南" => 1,
            "ai_west" or "ai_西" or "ai西" => 2,
            "ai_north" or "ai_北" or "ai北" => 3,
            _ => -1
        };
        return player >= 0;
    }

    public string? ArchiveSession(string session)
    {
        if (string.IsNullOrWhiteSpace(session))
        {
            return null;
        }

        var safeSession = Sanitize(session);
        var source = Path.Combine(LogsDirectory, safeSession);
        if (!System.IO.Directory.Exists(source))
        {
            return null;
        }

        System.IO.Directory.CreateDirectory(LogsDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var destination = Path.Combine(LogsDirectory, $"archive_{safeSession}_{timestamp}.tar.gz");

        try
        {
            using var file = File.Create(destination);
            using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
            using var tar = new TarWriter(gzip);
            foreach (var path in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, path).Replace('\\', '/');
                tar.WriteEntry(path, safeSession + "/" + relative);
            }

            System.IO.Directory.Delete(source, recursive: true);
            return destination;
        }
        catch
        {
            try
            {
                File.Delete(destination);
            }
            catch
            {
                // best effort, same as server.py
            }

            return null;
        }
    }

    public static string Sanitize(string? value)
        => UnsafePathChars.Replace(value ?? string.Empty, "-");

    private static JsonObject MergeCopy(JsonObject target, JsonObject source)
    {
        Merge(target, source);
        return target;
    }

    private void AppendLine(string path, string text)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lock (_appendLock)
        {
            File.AppendAllText(path, text + "\n", Utf8NoBom);
        }
    }
}
