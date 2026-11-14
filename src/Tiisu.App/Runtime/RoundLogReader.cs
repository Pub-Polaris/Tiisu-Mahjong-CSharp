using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Tiisu.Engine;

namespace Tiisu.App.Runtime;

public sealed record ReplayHeader(int Player, string Name, string Wind, bool Dealer);
public sealed record ReplayStep(int Player, string Action, string? Tile, string? Hand, int Wall, string? Reason, string? Via, IReadOnlyList<string> Rivers);
public sealed record ReplayRound(string FileName, int Round, int Honba, int RoundWind, IReadOnlyList<ReplayHeader> Headers, IReadOnlyList<ReplayStep> Steps);

/// <summary>读取 roundlog_*.jsonl 的最后一局，供牌谱回看页使用。</summary>
public static class RoundLogReader
{
    public static ReplayRound? LoadLatest(string? directory = null) =>
        LoadLatestRounds(directory)?.Rounds.LastOrDefault();

    /// <summary>读取最新一份 roundlog 的全部局；返回 null 表示还没有日志。</summary>
    public static (string FileName, IReadOnlyList<ReplayRound> Rounds)? LoadLatestRounds(string? directory = null)
    {
        string logs = new RoundLogWriter(directory).LogsDirectory;
        if (!Directory.Exists(logs))
        {
            return null;
        }

        string? file = Directory.EnumerateFiles(logs, RoundLogWriter.FilePrefix + "*" + RoundLogWriter.FileExtension)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
        if (file is null)
        {
            return null;
        }

        try
        {
            var rounds = Parse(File.ReadLines(file), Path.GetFileName(file));
            return rounds.Count == 0 ? null : (Path.GetFileName(file), rounds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<ReplayRound> Parse(IEnumerable<string> lines, string fileName = "")
    {
        var rounds = new List<ReplayRound>();
        var headers = new List<ReplayHeader>();
        var steps = new List<ReplayStep>();
        int round = 0;
        int honba = 0;
        int roundWind = 0;

        void Flush()
        {
            if (headers.Count == 0 && steps.Count == 0)
            {
                return;
            }

            rounds.Add(new ReplayRound(fileName, round, honba, roundWind, headers.ToArray(), steps.ToArray()));
            headers.Clear();
            steps.Clear();
        }

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject? node;
            try
            {
                node = JsonNode.Parse(line) as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }

            if (node is null)
            {
                continue;
            }

            string type = node["type"]?.GetValue<string>() ?? string.Empty;
            if (type == "round")
            {
                if (headers.Count >= 4)
                {
                    Flush();
                }

                round = Int(node["round"]);
                honba = Int(node["honba"]);
                roundWind = Int(node["roundWindIdx"]);
                headers.Add(new ReplayHeader(
                    Int(node["player"]),
                    SlugName(node["name"]?.GetValue<string>()),
                    WindName(Int(node["wind"])),
                    node["dealer"]?.GetValue<bool>() ?? false));
                continue;
            }

            if (type != "step")
            {
                continue;
            }

            if (headers.Count < 4)
            {
                continue;
            }

            var rivers = new List<string>();
            if (node["rivers"] is JsonArray array)
            {
                foreach (JsonNode? river in array)
                {
                    if (river is JsonArray tiles)
                    {
                        rivers.Add(string.Join(" ", tiles.Select(t => t?.GetValue<string>() ?? string.Empty)));
                    }
                    else
                    {
                        rivers.Add(river?.ToString() ?? string.Empty);
                    }
                }
            }

            steps.Add(new ReplayStep(
                Int(node["p"]),
                node["a"]?.GetValue<string>() ?? string.Empty,
                node["t"]?.GetValue<string>(),
                node["hand"]?.GetValue<string>(),
                Int(node["wall"]),
                node["reason"]?.GetValue<string>(),
                node["via"]?.GetValue<string>(),
                rivers));
        }

        Flush();
        return rounds;
    }

    public static string ActionName(string action) => action switch
    {
        "draw" => "摸牌",
        "discard" => "打出",
        "pon" => "碰",
        "chi" => "吃",
        "kan_minkan" => "明杠",
        "kan_ankan" => "暗杠",
        "kan_kakan" => "加杠",
        "rinshan_draw" => "岭上摸",
        "win_tsumo" => "自摸",
        "win_ron" => "荣和",
        "observe" => "观察",
        "special_flow" => "特殊流局",
        "flow" => "流局",
        "tiisuin_win" => "大七星",
        "witness" => "见证",
        "call" => "报听",
        "call_foul" => "错和",
        _ => action,
    };

    public static string PlayerName(IReadOnlyList<ReplayHeader> headers, int player)
    {
        ReplayHeader? header = headers.FirstOrDefault(h => h.Player == player);
        return header?.Name ?? ("座位 " + player.ToString(CultureInfo.InvariantCulture));
    }

    private static int Int(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out int i))
            {
                return i;
            }

            if (value.TryGetValue<long>(out long l))
            {
                return (int)l;
            }
        }

        return 0;
    }

    private static string WindName(int wind) => wind switch
    {
        0 => "东",
        1 => "南",
        2 => "西",
        3 => "北",
        _ => wind.ToString(CultureInfo.InvariantCulture),
    };

    private static string SlugName(string? slug) => slug switch
    {
        "human" => "你",
        "ai_east" => "AI 东",
        "ai_south" => "AI 南",
        "ai_west" => "AI 西",
        "ai_north" => "AI 北",
        null or "" => "AI",
        _ => slug,
    };
}
