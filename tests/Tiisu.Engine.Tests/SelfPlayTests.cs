using Tiisu.Engine;
using Tiisu.Engine.Deal;
using Tiisu.Engine.Random;
using Tiisu.Engine.SelfPlay;
using Xunit.Abstractions;

namespace Tiisu.Engine.Tests;

/// <summary>
/// 自走（SelfPlayDriver）验收：
/// 200 个 seed 全部能打完一整场、每步不变量成立、同 seed 完全可复现、统计量在合理区间。
/// </summary>
public sealed class SelfPlayTests
{
    private const int SeedCount = 200;
    private const int ModeTarget = 2;
    private const int InitialScoreSum = 49000 * MahjongEngine.PlayerCount;

    private readonly ITestOutputHelper _output;

    public SelfPlayTests(ITestOutputHelper output) => _output = output;

    /// <summary>200 场只跑一次（同测试类内各 Fact 共享）。</summary>
    private static readonly Lazy<IReadOnlyList<SelfPlayResult>> Corpus = new(() =>
    {
        var results = new List<SelfPlayResult>(SeedCount);
        for (uint seed = 1; seed <= SeedCount; seed++)
        {
            results.Add(SelfPlayDriver.Run(seed, ModeTarget));
        }

        return results;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    [Fact]
    public void EverySeed_FinishesWholeMatch()
    {
        var failures = new List<string>();
        foreach (var result in Corpus.Value)
        {
            try
            {
                Assert.True(result.Rounds > 0, $"seed {result.Seed} 一局都没打");
                Assert.Equal(result.Rounds, result.WinRounds + result.DrawRounds);
                Assert.Equal(MahjongEngine.PlayerCount, result.FinalScores.Count);
                Assert.Equal(64, result.EventHash.Length);
                Assert.Contains(result.Events, e => e.Type == GameEventTypes.RoundEnd);
                Assert.Equal(result.Rounds, result.Events.Count(e => e.Type == GameEventTypes.RoundEnd));
            }
            catch (Exception ex)
            {
                failures.Add($"seed {result.Seed}: {ex.Message}");
            }
        }

        _output.WriteLine($"seed 1..{SeedCount} 全部结束；总事件数 = {Corpus.Value.Sum(r => r.Events.Count)}");
        Assert.True(failures.Count == 0, "自走失败：\n" + string.Join("\n", failures.Take(20)));
    }

    [Fact]
    public void Invariants_HoldAtEveryStep()
    {
        int checks = 0;
        for (uint seed = 1; seed <= 20; seed++)
        {
            int lastRound = -1;
            int lastWall = MahjongEngine.TotalTiles;
            int roundBaseScoreSum = InitialScoreSum;

            SelfPlayDriver.Run(seed, new SelfPlayOptions
            {
                ModeTarget = ModeTarget,
                Observer = engine =>
                {
                    if (engine.GameOver)
                    {
                        return; // 和牌后荣和张会同时留在弃牌与胜者暗手里（JS 同口径），136 的物理不变量只在行牌中成立
                    }

                    checks++;

                    int wall = engine.Wall.Count;
                    if (engine.HandNumber != lastRound)
                    {
                        lastRound = engine.HandNumber;
                        lastWall = MahjongEngine.TotalTiles;
                        // 自摸余数归自摸者（engine.js splitPayment）会让跨局总分 +0..2，因此按局基准校验。
                        roundBaseScoreSum = engine.Scores.Sum();
                    }

                    Assert.InRange(wall, 0, MahjongEngine.TotalTiles - MahjongEngine.DeadWallMaCount);
                    Assert.True(wall <= lastWall, $"墙长回增：{wall} > {lastWall}（第 {engine.HandNumber} 局）");
                    lastWall = wall;

                    // 牌总数 = 手牌 + 副露 + 弃牌 + 墙（含王堆） + 岭上，行牌中恒为 136。
                    // 副露的那张牌仍留在放铳者的弃牌里（JS 快照口径），所以要扣掉每组的 1 张重复。
                    int total = wall + engine.Rinshan.Count;
                    int claimedMelds = 0;
                    for (int i = 0; i < MahjongEngine.PlayerCount; i++)
                    {
                        var player = engine.Players[i];
                        total += player.Hand.Count + player.Discards.Count;
                        foreach (var meld in player.Melds)
                        {
                            total += meld.Tiles.Count;
                            if (meld.Type != MeldType.Ankan)
                            {
                                claimedMelds++;
                            }
                        }
                    }

                    Assert.Equal(MahjongEngine.TotalTiles, total - claimedMelds);
                    Assert.Equal(roundBaseScoreSum, engine.Scores.Sum());

                    for (int i = 0; i < MahjongEngine.PlayerCount; i++)
                    {
                        var player = engine.Players[i];
                        int kong = player.KongCount();
                        int totalTiles = player.TotalTiles();
                        Assert.True(
                            totalTiles == 13 + kong || totalTiles == 14 + kong,
                            $"玩家 {i} 手牌 {totalTiles} 张（杠 {kong}）不合法");
                        Assert.All(player.Hand, tile => Assert.True(tile.Index is >= 0 and < 36));
                    }
                },
            });
        }

        _output.WriteLine($"逐步不变量检查 {checks} 次（seed 1..20，ModeTarget={ModeTarget}）");
        Assert.True(checks > 1000, $"观察点过少：{checks}");
    }

    [Fact]
    public void SameSeed_ProducesSameEventHash_DifferentSeedDiffers()
    {
        for (uint seed = 1; seed <= 10; seed++)
        {
            var first = SelfPlayDriver.Run(seed, ModeTarget);
            var second = SelfPlayDriver.Run(seed, ModeTarget);
            Assert.Equal(first.EventHash, second.EventHash);
            Assert.Equal(first.Events.Count, second.Events.Count);
            Assert.Equal(first.FinalScores, second.FinalScores);
        }

        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (uint seed = 1; seed <= 10; seed++)
        {
            hashes.Add(SelfPlayDriver.Run(seed, ModeTarget).EventHash);
        }

        Assert.Equal(10, hashes.Count);
        _output.WriteLine("同 seed 哈希一致；seed 1..10 哈希两两不同");
    }

    [Fact]
    public void RealWall_IsConsistentWithFactory()
    {
        var layout = RoundDealFactory.Cut(0, new Mulberry32Random(12345));

        Assert.Equal(136, layout.ShuffledDeck.Count);
        Assert.Equal(4, layout.Wall.Rinshan.Count);
        Assert.Equal(layout.SkipTiles - 4, layout.Wall.WangDui.Count);
        Assert.Equal(132, layout.Wall.Wall.Count);

        // 配牌 4×13 + 庄家多 1。
        Assert.Equal(MahjongEngine.PlayerCount, layout.Deal.Hands.Count);
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            Assert.Equal(i == layout.Deal.DealerIndex ? 14 : 13, layout.Deal.Hands[i].Count);
        }

        var engine = new MahjongEngine(randomSource: new Mulberry32Random(1));
        engine.SetMode(1);
        engine.HandNumber++;
        engine.StartNewRound(layout.Deal);

        Assert.Equal(79, engine.Wall.Count);
        Assert.Equal(4, engine.Rinshan.Count);
        Assert.All(engine.Wall, tile => Assert.NotNull(tile));
        Assert.All(engine.Rinshan, tile => Assert.NotNull(tile));

        // 引擎剩的 79 张必须就是工厂剩余牌山（从末尾扣掉配牌 53 张后的前缀），顺序一致。
        Assert.Equal(layout.Wall.Wall.Take(79), engine.Wall.Select(tile => tile!.Value));
    }

    /// <summary>定庄：&lt;7 顺时针、=7 本人、&gt;7 逆时针，掷骰者固定 0 号。</summary>
    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(1, 2, 3)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(3, 4, 0)]
    [InlineData(4, 5, 3)]
    [InlineData(5, 6, 1)]
    [InlineData(6, 6, 0)]
    public void DetermineDealer_MatchesEngineJs(int d1, int d2, int expected)
    {
        Assert.Equal(expected, TileSetFactory.DetermineDealer(new[] { d1, d2 }));
    }

    [Fact]
    public void TileSet_IsStandard136()
    {
        var deck = TileSetFactory.CreateDeck();
        Assert.Equal(136, deck.Count);
        Assert.Equal(34, deck.Distinct().Count());
        Assert.All(deck.GroupBy(tile => tile), group => Assert.Equal(4, group.Count()));

        var copy = deck.ToList();
        TileSetFactory.Shuffle(copy, new Mulberry32Random(7));
        Assert.Equal(deck.Count, copy.Count);
        Assert.Equal(deck.OrderBy(tile => tile.Index), copy.OrderBy(tile => tile.Index)); // 洗牌只换顺序
    }

    [Fact]
    public void Statistics_ArePlausible()
    {
        var results = Corpus.Value;
        int rounds = results.Sum(r => r.Rounds);
        int winRounds = results.Sum(r => r.WinRounds);
        int tsumo = results.Sum(r => r.TsumoWins);
        int ron = results.Sum(r => r.RonWins);
        int special = results.Sum(r => r.SpecialFlowRounds);
        int busts = results.Count(r => r.Busted);
        double winRate = (double)winRounds / rounds;
        double avgRounds = (double)rounds / results.Count;
        double avgTsumo = (double)tsumo / rounds;
        double avgRon = (double)ron / rounds;

        _output.WriteLine(
            $"200 场：总 {rounds} 局，和牌 {winRounds} 局（{winRate:P1}），流局 {rounds - winRounds} 局，特殊流局 {special} 局，" +
            $"出岭 {busts} 场；平均 {avgRounds:F2} 局/场；自摸 {avgTsumo:P1} / 荣和 {avgRon:P1}（每局）");

        var flowDetails = results
            .SelectMany(r => r.Events)
            .Where(e => e.Type == GameEventTypes.Flow)
            .GroupBy(e => e.Detail ?? "?")
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key + "=" + g.Count());
        int draws = results.SelectMany(r => r.Events).Count(e => e.Type == GameEventTypes.Draw);
        int discards = results.SelectMany(r => r.Events).Count(e => e.Type == GameEventTypes.Discard);
        int melds = results.SelectMany(r => r.Events).Count(e => e.Type == GameEventTypes.Meld);
        int kans = results.SelectMany(r => r.Events).Count(e => e.Type == GameEventTypes.Kan);
        _output.WriteLine($"流局类型：{string.Join(" / ", flowDetails)}");
        _output.WriteLine($"事件：draw={draws} discard={discards} meld={melds} kan={kans}");

        // 既不能 0 和牌，也不能全和（留出流局）；平均局数应明显多于 ModeTarget（连庄 / 延长），但不会失控。
        Assert.InRange(winRate, 0.20, 0.995);
        Assert.True(winRounds < rounds, "200 场里一局流局都没有，疑似流局路径未覆盖");
        Assert.True(results.Sum(r => r.DrawRounds) > 0, "流局数必须 > 0");
        Assert.InRange(avgRounds, 1.0, 20.0);
        Assert.InRange(avgTsumo, 0.05, 0.90);
        Assert.InRange(avgRon, 0.01, 1.60);
        foreach (var result in results)
        {
            Assert.InRange(result.Rounds, 1, ModeTarget + 40);

            if (result.Busted)
            {
                // 出岭：负分被记为 −7000，总分守恒被规则本身打破，只核对标记与点数一致。
                Assert.Contains(-7000, result.FinalScores);
                continue;
            }

            // 自摸余数归自摸者：每局最多 +2，跨局总分只会缓慢上浮；无出岭时不得出现其它漂移。
            Assert.InRange(result.FinalScores.Sum(), InitialScoreSum, InitialScoreSum + (2 * result.TsumoWins));
        }
    }
}
