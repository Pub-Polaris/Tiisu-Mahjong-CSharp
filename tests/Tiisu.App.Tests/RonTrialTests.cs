using System.Reflection;
using Tiisu.App.Runtime;
using Tiisu.Core;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>
/// 荣和试探/过牌不能动玩家手牌：铳张留在牌河，按下和牌时才补进手里。
/// 回归：别家打出 3m、手里有两张 3m，过牌后不能少一张（小相公）。
/// </summary>
public sealed class RonTrialTests
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

    private static (MatchRuntime Runtime, int Human) NewRuntime()
    {
        var settings = new Settings { RecordTiles = false };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(33), _ => { }, null, null, null, 33)
        {
            AutoPilot = false,
        };
        runtime.StartMatch(4);
        return (runtime, runtime.HumanIndex);
    }

    [Fact]
    public void TryRon_DoesNotInsertDiscardIntoHand()
    {
        var (runtime, human) = NewRuntime();
        var player = runtime.Engine.Players[human];
        player.Hand.Clear();
        foreach (var wire in new[] { "1m", "1m", "1m", "2m", "2m", "2m", "3m", "3m", "5m", "5m", "5m", "7m", "7m" })
        {
            player.Hand.Add(T(wire));
        }
        player.Hand.Sort();

        var result = runtime.TryRon(human, T("3m"));

        Assert.NotNull(result);
        Assert.Equal(13, player.Hand.Count);
        Assert.Equal(2, player.Hand.Count(t => t == T("3m")));
    }

    [Fact]
    public void TryRon_NonWinningDiscard_LeavesHandUnchanged()
    {
        var (runtime, human) = NewRuntime();
        var player = runtime.Engine.Players[human];
        player.Hand.Clear();
        foreach (var wire in new[] { "1m", "1m", "1m", "2m", "2m", "2m", "3m", "3m", "5m", "5m", "5m", "7m", "7m" })
        {
            player.Hand.Add(T(wire));
        }
        player.Hand.Sort();
        var before = player.Hand.ToArray();

        var result = runtime.TryRon(human, T("9s"));

        Assert.Null(result);
        Assert.Equal(before, player.Hand.ToArray());
    }

    [Fact]
    public void PassRon_KeepsHandAndShanponWait()
    {
        var (runtime, human) = NewRuntime();
        var player = runtime.Engine.Players[human];
        player.Hand.Clear();
        foreach (var wire in new[] { "1m", "1m", "1m", "2m", "2m", "2m", "3m", "3m", "5m", "5m", "5m", "7m", "7m" })
        {
            player.Hand.Add(T(wire));
        }
        player.Hand.Sort();

        var result = runtime.TryRon(human, T("3m"));
        Assert.NotNull(result);

        var phaseField = typeof(MatchRuntime).GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance)!;
        phaseField.SetValue(runtime, MatchPhase.HumanRon);
        var pendingField = typeof(MatchRuntime).GetField("_pendingRon", BindingFlags.NonPublic | BindingFlags.Instance)!;
        pendingField.SetValue(runtime, new PendingRon(T("3m"), result, (human + 1) % 4));

        runtime.Pass();

        Assert.Equal(13, player.Hand.Count);
        Assert.Equal(2, player.Hand.Count(t => t == T("3m")));
        Assert.Equal(2, player.Hand.Count(t => t == T("7m")));
    }
}
