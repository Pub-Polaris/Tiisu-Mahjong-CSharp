using System.Text.Json.Nodes;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class StatsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tiisu-stats-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    private static RoundResult Result(string kind, params int[] winners) =>
        new(1, kind, winners, null, new[] { 50000, 49000, 49000, 49000 }, 0, 0, false);

    private static readonly string[] Names = { "你", "AI 南", "AI 西", "AI 北" };

    [Fact]
    public void RecordRound_AccumulatesAndPersists()
    {
        var store = new StatsStore(_dir);

        store.RecordRound(Result("tsumo", 0), Names);
        store.RecordRound(Result("ron", 1), Names);
        store.RecordRound(Result("flow"), Names);

        var stats = store.Load();
        Assert.Equal(3, stats["rounds"]!.GetValue<int>());
        Assert.Equal(2, stats["wins"]!.GetValue<int>());
        Assert.Equal(1, stats["tsumoWins"]!.GetValue<int>());
        Assert.Equal(1, stats["ronWins"]!.GetValue<int>());
        Assert.Equal(1, stats["drawRounds"]!.GetValue<int>());

        var reloaded = new StatsStore(_dir).Load();
        Assert.Equal(3, reloaded["rounds"]!.GetValue<int>());
        Assert.Equal(1, reloaded["byPlayer"]!["你"]!["wins"]!.GetValue<int>());
        Assert.Equal(1, reloaded["byPlayer"]!["AI 南"]!["wins"]!.GetValue<int>());
    }

    [Fact]
    public void RecordMatchEnd_AddsCumulativePoints()
    {
        var store = new StatsStore(_dir);

        store.RecordMatchEnd(new[] { 56000, 48000, 47000, 45000 }, Names);

        var stats = store.Load();
        Assert.Equal(196000, stats["cumulativePoints"]!.GetValue<long>());
        Assert.Equal(56000, stats["byPlayer"]!["你"]!["points"]!.GetValue<long>());
    }

    [Fact]
    public void CorruptStatsFile_IsMovedAsideAndDefaultsUsed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "stats.json"), "{broken json");

        var stats = new StatsStore(_dir).Load();

        Assert.Equal(0, stats["rounds"]!.GetValue<int>());
        Assert.True(File.Exists(Path.Combine(_dir, "stats.json.corrupt")));
    }

    [Fact]
    public void Reset_ClearsCounters()
    {
        var store = new StatsStore(_dir);
        store.RecordRound(Result("tsumo", 0), Names);

        store.Reset();

        var stats = store.Load();
        Assert.Equal(0, stats["rounds"]!.GetValue<int>());
        Assert.Equal(0, stats["wins"]!.GetValue<int>());
        Assert.Empty(stats["byPlayer"]!.AsObject());
    }
}
