using Tiisu.Engine;
using Tiisu.Engine.Replay;

namespace Tiisu.Engine.Tests;

/// <summary>回放式 golden：705 个事件逐事件比对快照（状态 + 四家牌面），要求 0 差异。</summary>
public sealed class EngineGoldenTests
{
    private const int ExpectedEventCount = 705;

    private static ReplayCorpus LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine-golden.json");
        Assert.True(File.Exists(path), $"找不到语料: {path}");
        return ReplayCorpus.LoadFromFile(path);
    }

    [Fact]
    public void Replay_EveryEventSnapshot_Matches()
    {
        var corpus = LoadCorpus();
        var report = ReplayDriver.Run(corpus);

        Assert.Equal(ExpectedEventCount, corpus.Events.Count);
        Assert.Equal(corpus.Events.Count, report.Processed);
        Assert.True(report.Clean, report.Describe());
    }

    [Fact]
    public void Replay_IsDeterministic()
    {
        var corpus = LoadCorpus();
        var first = ReplayDriver.Run(corpus);
        var second = ReplayDriver.Run(corpus);

        Assert.Equal(first.Diffs.Count, second.Diffs.Count);
        Assert.Equal(first.Processed, second.Processed);
    }

    /// <summary>反向校验：故意改坏一个快照字段，驱动必须报出这条差异（避免比对退化成空操作）。</summary>
    [Fact]
    public void Replay_ReportsCorruptedSnapshot()
    {
        var corpus = LoadCorpus();
        var target = corpus.Events[^1];
        target.Snapshot.Wall += 7;
        target.Snapshot.Scores[2] += 100;

        var report = ReplayDriver.Run(corpus);

        Assert.False(report.Clean);
        Assert.Contains(report.Diffs, d => d.Field == "wall" && d.Expected == (target.Snapshot.Wall).ToString());
        Assert.Contains(report.Diffs, d => d.Field == "scores[2]");
    }

    /// <summary>赋位表（ike.json）边界：低于首档时按 700/赋 递增，28 赋封顶 35000。</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 4200)]
    [InlineData(7, 7000)]
    [InlineData(13, 11200)]
    [InlineData(14, 14000)]
    [InlineData(21, 28000)]
    [InlineData(27, 32200)]
    [InlineData(28, 35000)]
    [InlineData(30, 35000)]
    public void CalcPoints_MatchesIkePointsTable(int fu, int expected)
    {
        var engine = new MahjongEngine();
        Assert.Equal(expected, engine.CalcPoints(fu));
    }

    /// <summary>三家均摊取整：余数归自摸者。</summary>
    [Theory]
    [InlineData(7700, 2566, 2)]
    [InlineData(12600, 4200, 0)]
    [InlineData(11900, 3966, 2)]
    public void SplitPayment_PutsRemainderOnWinner(int total, int each, int remainder)
    {
        var engine = new MahjongEngine();
        var (actualEach, actualRemainder) = engine.SplitPayment(total);
        Assert.Equal(each, actualEach);
        Assert.Equal(remainder, actualRemainder);
    }
}
