using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Tiisu.Engine;

namespace Tiisu.App.Runtime;

public sealed record ReplayMeld(string Type, IReadOnlyList<string> Tiles, int ClaimedIndex = -1);

public sealed record ReplayHeader(
    int Player,
    string Name,
    string Wind,
    bool Dealer,
    bool Human = false,
    string? DisplayName = null);

public sealed record ReplayStep(
    int Player,
    string Action,
    string? Tile,
    string? Hand,
    int Wall,
    string? Reason,
    string? Via,
    IReadOnlyList<string> Rivers,
    IReadOnlyList<string?>? AllHands = null,
    IReadOnlyList<IReadOnlyList<ReplayMeld>>? AllMelds = null,
    IReadOnlyList<int>? HandCounts = null,
    IReadOnlyList<int>? Scores = null,
    int? Turn = null,
    IReadOnlyList<ReplayMeld>? Melds = null,
    IReadOnlyList<int>? MeldCounts = null);

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
                    WindName(WindIndex(node["wind"])),
                    node["dealer"]?.GetValue<bool>() ?? false,
                    node["human"]?.GetValue<bool>() ?? (Int(node["player"]) == 0),
                    node["nameText"]?.GetValue<string>()));
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

            var allHands = StringArray(node["allHands"]);
            var handCounts = IntArray(node["handCounts"]);
            var scores = IntArray(node["scores"]);
            var meldCounts = IntArray(node["meldCounts"]);
            var allMelds = MeldsMatrix(node["allMelds"]);
            var melds = Melds(node["melds"]);
            int? turn = node["turn"] is null ? null : Int(node["turn"]);

            steps.Add(new ReplayStep(
                Int(node["p"]),
                node["a"]?.GetValue<string>() ?? string.Empty,
                node["t"]?.GetValue<string>(),
                node["hand"]?.GetValue<string>(),
                Int(node["wall"]),
                node["reason"]?.GetValue<string>(),
                node["via"]?.GetValue<string>(),
                rivers,
                allHands,
                allMelds,
                handCounts,
                scores,
                turn ?? Int(node["p"]),
                melds,
                meldCounts));
        }

        Flush();
        return rounds;
    }

    private static IReadOnlyList<ReplayMeld>? Melds(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return null;
        }

        var result = new List<ReplayMeld>(array.Count);
        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject meld)
            {
                continue;
            }

            var tiles = new List<string>();
            if (meld["tiles"] is JsonArray tileArray)
            {
                foreach (JsonNode? tile in tileArray)
                {
                    string? value = tile?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        tiles.Add(value!);
                    }
                }
            }

            if (tiles.Count == 0)
            {
                string? compact = meld["t"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(compact))
                {
                    tiles.AddRange(ExpandCompactMeld(compact!));
                }
            }

            if (tiles.Count == 0)
            {
                continue;
            }

            result.Add(new ReplayMeld(
                meld["k"]?.GetValue<string>() ?? "pon",
                tiles,
                meld["claimed"] is null ? -1 : Int(meld["claimed"])));
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<ReplayMeld>>? MeldsMatrix(JsonNode? node)
    {
        if (node is not JsonArray seats)
        {
            return null;
        }

        var result = new List<IReadOnlyList<ReplayMeld>>(seats.Count);
        foreach (JsonNode? seat in seats)
        {
            result.Add(Melds(seat) ?? Array.Empty<ReplayMeld>());
        }

        return result;
    }

    private static IReadOnlyList<string>? StringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return null;
        }

        var result = new List<string>(array.Count);
        foreach (JsonNode? item in array)
        {
            result.Add(item?.GetValue<string>() ?? string.Empty);
        }

        return result;
    }

    private static IReadOnlyList<int>? IntArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return null;
        }

        var result = new List<int>(array.Count);
        foreach (JsonNode? item in array)
        {
            result.Add(Int(item));
        }

        return result;
    }

    /// <summary>旧日志的副露只有 "123m" 这种压缩串，展开成 1m/2m/3m。</summary>
    private static IEnumerable<string> ExpandCompactMeld(string compact)
    {
        if (compact.Length < 2)
        {
            yield break;
        }

        char suit = compact[^1];
        for (int i = 0; i < compact.Length - 1; i++)
        {
            char c = compact[i];
            if (c is >= '1' and <= '9')
            {
                yield return c.ToString() + suit;
            }
        }
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
        if (header is null)
        {
            return "座位 " + player.ToString(CultureInfo.InvariantCulture);
        }

        return string.IsNullOrWhiteSpace(header.DisplayName) ? header.Name : header.DisplayName!;
    }

    private static int WindIndex(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out string? wire))
        {
            return wire switch
            {
                "east" => 0,
                "south" => 1,
                "west" => 2,
                "north" => 3,
                _ => 0,
            };
        }

        return Int(node);
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
