using Tiisu.App.Runtime;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class SnapshotRevisionTests
{
    [Fact]
    public void StaleCommand_IsRejectedWithoutChangingState()
    {
        var settings = new Settings { RecordTiles = false };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(44), _ => { }, null, null, null, 44)
        {
            AutoPilot = false,
        };
        runtime.StartMatch(4);

        int human = runtime.HumanIndex;
        int before = runtime.Engine.Players[human].Hand.Count;

        runtime.Execute(new GameCommand(GameCommandKind.Discard, Index: 0, SnapshotRevision: 1));

        Assert.Equal(before, runtime.Engine.Players[human].Hand.Count);
        Assert.Equal(MatchPhase.HumanDiscard, runtime.Phase);
    }

    [Fact]
    public void CurrentRevisionCommand_IsAccepted()
    {
        var settings = new Settings { RecordTiles = false };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(45), _ => { }, null, null, null, 45)
        {
            AutoPilot = false,
        };
        runtime.StartMatch(4);

        long before = runtime.Snapshot.Revision;

        runtime.Execute(new GameCommand(
            GameCommandKind.Discard,
            Index: 0,
            SnapshotRevision: before));

        Assert.True(runtime.Snapshot.Revision > before);
    }
}
