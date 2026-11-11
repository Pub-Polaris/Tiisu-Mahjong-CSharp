using Tiisu.Core;
using Xunit;

namespace Tiisu.Core.Tests;

/// <summary>共享 WinChecker 的并发安全（审计 P2-2 的临时保护：整段判定串行）。</summary>
public sealed class WinCheckerConcurrencyTests
{
    private static WinChecker NewChecker() => new(YakuData.LoadDefault());

    [Fact]
    public async Task 多线程共用同一实例_结果与单线程一致()
    {
        var checker = NewChecker();
        var hand = new[]
        {
            Tile.Parse("m1"), Tile.Parse("m2"), Tile.Parse("m3"),
            Tile.Parse("m4"), Tile.Parse("m5"), Tile.Parse("m6"),
            Tile.Parse("m7"), Tile.Parse("m8"), Tile.Parse("m9"),
            Tile.Parse("p1"), Tile.Parse("p1"), Tile.Parse("p1"),
            Tile.Parse("s5"), Tile.Parse("s5"),
        };

        var expected = checker.CheckWin(hand);

        var results = new System.Collections.Concurrent.ConcurrentBag<WinResult>();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                results.Add(checker.CheckWin(hand));
            }
        }));
        await Task.WhenAll(tasks);

        Assert.All(results, r =>
        {
            Assert.Equal(expected.Success, r.Success);
            Assert.Equal(expected.TotalFu, r.TotalFu);
            Assert.Equal(expected.ActiveYaku.Count, r.ActiveYaku.Count);
        });
    }
}
