using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Tiisu.App.Runtime;

public sealed record PlayerStats(string Name, int Wins, int Tiisuin, int Witness, long Points);

/// <summary>战绩页的只读聚合：把 stats.json 转成可直接绘制的数字。</summary>
public sealed record StatsSummary(
    int Rounds,
    int Wins,
    int TsumoWins,
    int RonWins,
    int DrawRounds,
    int SpecialFlowRounds,
    int TiisuinCount,
    int WitnessCount,
    long CumulativePoints,
    IReadOnlyList<PlayerStats> Players)
{
    public double WinRate => Rounds > 0 ? (double)Wins / Rounds : 0;
    public double TsumoRate => Wins > 0 ? (double)TsumoWins / Wins : 0;
    public double DrawRate => Rounds > 0 ? (double)DrawRounds / Rounds : 0;

    public static StatsSummary From(JsonObject stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var players = new List<PlayerStats>();
        if (stats["byPlayer"] is JsonObject byPlayer)
        {
            foreach (var pair in byPlayer)
            {
                if (pair.Value is not JsonObject entry)
                {
                    continue;
                }

                players.Add(new PlayerStats(
                    pair.Key,
                    Int(entry["wins"]),
                    Int(entry["tiisuin"]),
                    Int(entry["witness"]),
                    Long(entry["points"])));
            }
        }

        players.Sort((a, b) =>
        {
            int wins = b.Wins.CompareTo(a.Wins);
            return wins != 0 ? wins : b.Points.CompareTo(a.Points);
        });

        return new StatsSummary(
            Int(stats["rounds"]),
            Int(stats["wins"]),
            Int(stats["tsumoWins"]),
            Int(stats["ronWins"]),
            Int(stats["drawRounds"]),
            Int(stats["specialFlowRounds"]),
            Int(stats["tiisuinCount"]),
            Int(stats["witnessCount"]),
            Long(stats["cumulativePoints"]),
            players);
    }

    public static StatsSummary Load(StatsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return From(store.Load());
    }

    private static int Int(JsonNode? node) => (int)Math.Clamp(Long(node), 0, int.MaxValue);

    private static long Long(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out int i))
            {
                return i;
            }

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
