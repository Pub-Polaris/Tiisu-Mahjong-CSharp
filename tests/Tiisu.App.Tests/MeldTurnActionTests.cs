using System.Reflection;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>吃/碰/明杠之后没有摸牌，必须先出牌，不能直接宣布和牌。</summary>
public sealed class MeldTurnActionTests
{
    [Fact]
    public void 副露后没有新摸牌_不出和牌按钮()
    {
        var runtime = new MatchRuntime(
            new Settings { RecordTiles = false },
            new Mulberry32Random(7),
            _ => { },
            saveStore: null,
            stats: null,
            roundLog: null,
            matchSeed: 7);
        var player = runtime.Engine.Players[runtime.HumanIndex];
        player.Hand.Clear();
        player.Melds.Clear();
        player.IsCalled = false;
        player.IsDealer = false;
        player.SkipDraw = false;
        player.LastDraw = null;
        foreach (string id in new[] { "z2", "z2", "z2", "z3", "z3", "z3", "z4", "z4", "z4", "z5", "z5" })
        {
            player.Hand.Add(Tile.Parse(id));
        }

        player.Melds.Add(new Tiisu.Engine.Meld(
            MeldType.Pon,
            new[] { Tile.Parse("z1"), Tile.Parse("z1"), Tile.Parse("z1") }));

        // 碰后 14 张（11 手牌 + 3 副露）本身已是和形，但没有摸牌，不能直接和。
        SetPhase(runtime, MatchPhase.HumanDiscard);
        Assert.False(runtime.AvailableActions().HasFlag(HumanAction.Tsumo));

        // 对照组：同一手牌补上摸牌标记后，和牌才应出现。
        player.LastDraw = Tile.Parse("z5");
        Assert.True(runtime.AvailableActions().HasFlag(HumanAction.Tsumo));
    }

    private static void SetPhase(MatchRuntime runtime, MatchPhase phase)
    {
        typeof(MatchRuntime)
            .GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(runtime, phase);
    }
}
