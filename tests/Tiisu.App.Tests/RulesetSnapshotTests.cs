using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Tiisu.Core;
using Tiisu.Engine;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class RulesetSnapshotTests
{
    [Fact]
    public void Snapshot_PicksUpCurrentSettings()
    {
        var engine = new MahjongEngine(ruleset: Ruleset.TuidaoHu);
        var vm = SnapshotBuilder.Build(engine, "human_discard", HumanAction.None, null, null,
            new Settings { Ruleset = Ruleset.TuidaoHu });
        Assert.Equal(Ruleset.TuidaoHu, vm.Ruleset);
    }

    [Fact]
    public void Snapshot_DefaultIsQizi()
    {
        var engine = new MahjongEngine(ruleset: Ruleset.Qizi);
        var vm = SnapshotBuilder.Build(engine, "human_discard", HumanAction.None, null, null,
            new Settings { Ruleset = Ruleset.Qizi });
        Assert.Equal(Ruleset.Qizi, vm.Ruleset);
    }
}