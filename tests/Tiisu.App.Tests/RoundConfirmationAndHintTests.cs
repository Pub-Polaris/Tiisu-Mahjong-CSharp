using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Tiisu.Core;
using Tiisu.Engine;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class RoundConfirmationGateTests
{
    [Fact]
    public void AiConfirmed_HumanNot_DoesNotAdvance()
    {
        var gate = new RoundConfirmationGate();
        gate.Confirm(1);
        gate.Confirm(2);
        gate.Confirm(3);

        Assert.False(gate.AllConfirmed);
        Assert.Equal(new[] { 0 }, gate.Pending);
    }

    [Fact]
    public void AllConfirmed_AllowsAdvance()
    {
        var gate = new RoundConfirmationGate();
        gate.Confirm(0);
        gate.Confirm(1);
        gate.Confirm(2);
        gate.Confirm(3);

        Assert.True(gate.AllConfirmed);
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public void Reset_ClearsConfirmations()
    {
        var gate = new RoundConfirmationGate();
        gate.ConfirmAll();
        gate.Reset();

        Assert.False(gate.AllConfirmed);
        Assert.Equal(4, gate.Pending.Count);
    }
}

public sealed class WinHintTests
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

    private static TableViewModel Build(MahjongEngine engine, bool called)
    {
        var player = engine.Players[0];
        player.IsHuman = true;
        player.IsCalled = called;
        player.Hand.Clear();
        foreach (var wire in new[] { "1m", "2m", "3m", "4m", "5m", "6m", "2p", "3p", "4p", "5s", "6s", "7s", "5z", "5z" })
        {
            player.Hand.Add(T(wire));
        }

        player.Hand.Sort();
        return SnapshotBuilder.Build(engine, "human_discard", HumanAction.None, null, null, new Settings());
    }

    [Fact]
    public void WinningShapeWithoutYaku_NoHint()
    {
        var engine = new MahjongEngine(ruleset: Ruleset.Qizi);

        var vm = Build(engine, called: false);

        Assert.True(string.IsNullOrEmpty(vm.WinHint));
    }

    [Fact]
    public void CalledWinningShapeUnderThreshold_NoMisfireHint()
    {
        var engine = new MahjongEngine(ruleset: Ruleset.Qizi);

        var vm = Build(engine, called: true);

        Assert.True(string.IsNullOrEmpty(vm.WinHint));
    }
}
