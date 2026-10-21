using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tiisu.App.Runtime;

/// <summary>
/// 统计（<c>%APPDATA%\TiisuMahjong\stats.json</c>）。
/// 核心字段口径照 Web 侧 <c>engine.js</c> 的 <c>/api/stats</c> 上报（<c>reportStats</c> / <c>reportStatsRoundOnce</c> /
/// <c>advanceRound</c>）：<c>tiisuinCount</c> / <c>witnessCount</c> / <c>wins</c> / <c>rounds</c> / <c>byPlayer</c>。
/// <para>
/// 追加字段（旧 Web 端不写、读到时忽略即可）：<c>tsumoWins</c> / <c>ronWins</c> / <c>drawRounds</c> /
/// <c>specialFlowRounds</c> / <c>cumulativePoints</c>，以及 <c>byPlayer[name].points</c>（每场终局点数累计）。
/// </para>
/// </summary>
public sealed class StatsStore
{
    private const string DefaultStatsJson = """
    {
      "tiisuinCount": 0,
      "witnessCount": 0,
      "wins": 0,
      "rounds": 0,
      "tsumoWins": 0,
      "ronWins": 0,
      "drawRounds": 0,
      "specialFlowRounds": 0,
      "cumulativePoints": 0,
      "byPlayer": {}
    }
    """;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly object _gate = new();

    public StatsStore(string? directory = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? Settings.DefaultDirectory);
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string StatsPath => Path.Combine(DirectoryPath, "stats.json");

    public JsonObject Load()
    {
        lock (_gate)
        {
            return LoadCore();
        }
    }

    /// <summary>局末计一次（每手恰好一次，含和牌 / 流局 / 特殊流局 / 大七星）。</summary>
    public void RecordRound(RoundResult result, IReadOnlyList<string> playerNames)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            var stats = LoadCore();
            stats["rounds"] = Int(stats["rounds"]) + 1;

            switch (result.Kind)
            {
                case "tsumo":
                    stats["wins"] = Int(stats["wins"]) + 1;
                    stats["tsumoWins"] = Int(stats["tsumoWins"]) + 1;
                    AddPlayer(stats, Name(playerNames, result.Winners, 0), wins: 1);
                    break;

                case "ron":
                    stats["wins"] = Int(stats["wins"]) + result.Winners.Count;
                    stats["ronWins"] = Int(stats["ronWins"]) + result.Winners.Count;
                    for (int slot = 0; slot < result.Winners.Count; slot++)
                    {
                        AddPlayer(stats, Name(playerNames, result.Winners, slot), wins: 1);
                    }

                    break;

                case "tiisuin":
                    stats["tiisuinCount"] = Int(stats["tiisuinCount"]) + 1;
                    for (int slot = 0; slot < result.Winners.Count; slot++)
                    {
                        AddPlayer(stats, Name(playerNames, result.Winners, slot), tiisuin: 1);
                    }

                    // 大七星：其余三家各记一次 witness（JS：{witness: [三家姓名]} → witnessCount += 3）
                    for (int i = 0; i < playerNames.Count; i++)
                    {
                        if (result.Winners.Contains(i))
                        {
                            continue;
                        }

                        AddPlayer(stats, playerNames[i], witness: 1);
                        stats["witnessCount"] = Int(stats["witnessCount"]) + 1;
                    }

                    break;

                case "special_flow":
                    stats["drawRounds"] = Int(stats["drawRounds"]) + 1;
                    stats["specialFlowRounds"] = Int(stats["specialFlowRounds"]) + 1;
                    break;

                default:
                    stats["drawRounds"] = Int(stats["drawRounds"]) + 1;
                    break;
            }

            WriteCore(stats);
        }
    }

    /// <summary>整场终局：把四家最终点数累加进 <c>byPlayer[].points</c> 与 <c>cumulativePoints</c>。</summary>
    public void RecordMatchEnd(IReadOnlyList<int> scores, IReadOnlyList<string> playerNames)
    {
        ArgumentNullException.ThrowIfNull(scores);
        lock (_gate)
        {
            var stats = LoadCore();
            long total = 0;
            for (int i = 0; i < scores.Count && i < playerNames.Count; i++)
            {
                AddPlayer(stats, playerNames[i], points: scores[i]);
                total += scores[i];
            }

            stats["cumulativePoints"] = Int(stats["cumulativePoints"]) + total;
            WriteCore(stats);
        }
    }

    /// <summary>清空统计（调试 / 重置用）。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            WriteCore((JsonObject)JsonNode.Parse(DefaultStatsJson)!);
        }
    }

    private JsonObject LoadCore()
    {
        var stats = (JsonObject)JsonNode.Parse(DefaultStatsJson)!;
        if (!File.Exists(StatsPath))
        {
            return stats;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(StatsPath, Encoding.UTF8)) is JsonObject existing)
            {
                foreach (var pair in existing)
                {
                    stats[pair.Key] = pair.Value?.DeepClone();
                }
            }
        }
        catch
        {
            // 损坏的 stats.json 按默认值重建。
        }

        if (stats["byPlayer"] is not JsonObject)
        {
            stats["byPlayer"] = new JsonObject();
        }

        return stats;
    }

    private void WriteCore(JsonObject stats)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(StatsPath, stats.ToJsonString(WriteOptions), Utf8NoBom);
    }

    private static void AddPlayer(JsonObject stats, string name, long wins = 0, long tiisuin = 0, long witness = 0, long points = 0)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var byPlayer = stats["byPlayer"] as JsonObject;
        if (byPlayer is null)
        {
            byPlayer = new JsonObject();
            stats["byPlayer"] = byPlayer;
        }

        var entry = byPlayer[name] as JsonObject ?? new JsonObject();
        if (wins != 0)
        {
            entry["wins"] = Int(entry["wins"]) + wins;
        }

        if (tiisuin > 0)
        {
            entry["tiisuin"] = Int(entry["tiisuin"]) + tiisuin;
        }

        if (witness > 0)
        {
            entry["witness"] = Int(entry["witness"]) + witness;
        }

        if (points != 0)
        {
            entry["points"] = Int(entry["points"]) + points;
        }

        byPlayer[name] = entry;
    }

    private static string Name(IReadOnlyList<string> names, IReadOnlyList<int> winners, int slot) =>
        slot >= 0 && slot < winners.Count && winners[slot] >= 0 && winners[slot] < names.Count
            ? names[winners[slot]]
            : string.Empty;

    private static long Int(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out long l))
            {
                return l;
            }

            if (value.TryGetValue<double>(out double d))
            {
                return (long)d;
            }
        }

        return 0;
    }
}
