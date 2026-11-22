using System.Reflection;
using Tiisu.App.Runtime;
using Tiisu.Core;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>
/// 联机多家荣和：同一张弃牌上每个能荣和的人类座位都要拿到窗口，
/// 不能只看座位号最小的那一家。
/// </summary>
public sealed class NetworkRonTests
{
    private static Tile T(string wire)
    {
        var suit = wire[^1] switch
        {
            'm' => Suit.Man,
            'p' => Suit.Pin,
            's' => Suit.Sou,
            _ => Suit.Honor,
        };
        return new Tile(suit, wire[0] - '0');
    }

    private static object? Private(MatchRuntime runtime, string field) =>
        typeof(MatchRuntime).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime);

    [Fact]
    public void Two_humans_on_the_same_discard_both_get_a_ron_window()
    {
        var runtime = new MatchRuntime(new Settings { RecordTiles = false }, new Mulberry32Random(33), _ => { }, null, null, null, 33);
        runtime.ConfigureNetworkSeats(new[] { true, true, true, false });
        runtime.StartMatch(4);

        // 两家同样的 3m 单骑 / 双碰形：对同一张 3m 都能荣和。
        foreach (int seat in new[] { 1, 2 })
        {
            var hand = runtime.Engine.Players[seat].Hand;
            hand.Clear();
            foreach (string wire in new[] { "1m", "1m", "1m", "2m", "2m", "2m", "3m", "3m", "5m", "5m", "5m", "7m", "7m" })
            {
                hand.Add(T(wire));
            }

            hand.Sort();
        }

        typeof(MatchRuntime).GetMethod("AfterDiscard", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(runtime, new object[] { T("3m"), 0 });

        Assert.Equal(MatchPhase.HumanRon, runtime.Phase);
        Assert.Equal(1, (int)Private(runtime, "_activeHuman")!);
        Assert.NotNull(Private(runtime, "_pendingRon"));
        Assert.Single((System.Collections.IEnumerable)Private(runtime, "_ronWindows")!);

        runtime.Pass();

        // 第一家过牌后仍要给第二家开窗，而不是直接结算 / 进副露。
        Assert.Equal(MatchPhase.HumanRon, runtime.Phase);
        Assert.Equal(2, (int)Private(runtime, "_activeHuman")!);
        Assert.NotNull(Private(runtime, "_pendingRon"));
        Assert.Empty((System.Collections.IEnumerable)Private(runtime, "_ronWindows")!);
    }

    [Fact]
    public void Two_humans_who_claim_ron_are_settled_together()
    {
        var runtime = new MatchRuntime(new Settings { RecordTiles = false }, new Mulberry32Random(33), _ => { }, null, null, null, 33);
        runtime.ConfigureNetworkSeats(new[] { true, true, true, false });
        runtime.StartMatch(4);

        foreach (int seat in new[] { 1, 2 })
        {
            var hand = runtime.Engine.Players[seat].Hand;
            hand.Clear();
            foreach (string wire in new[] { "1m", "1m", "1m", "2m", "2m", "2m", "3m", "3m", "5m", "5m", "5m", "7m", "7m" })
            {
                hand.Add(T(wire));
            }

            hand.Sort();
        }

        typeof(MatchRuntime).GetMethod("AfterDiscard", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(runtime, new object[] { T("3m"), 0 });

        runtime.HumanRon();
        Assert.Equal(MatchPhase.HumanRon, runtime.Phase);
        Assert.Equal(2, (int)Private(runtime, "_activeHuman")!);

        runtime.HumanRon();

        // 两家都点荣和：一起结算（双和），不能只算第一家。
        Assert.Equal("ron", runtime.LastWinKind);
        Assert.True(runtime.Engine.GameOver);
    }
}
