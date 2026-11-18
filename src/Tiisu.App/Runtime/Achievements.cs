using System;
using System.Collections.Generic;
using System.Linq;
using Tiisu.App.Runtime;
using Tiisu.Localization;

namespace Tiisu.App.Runtime;

public sealed record Achievement(string Id, string Title, string Description, string Progress, bool Unlocked);

/// <summary>从本机统计派生成就；不单独持久化，规则变化时可直接重算。</summary>
public static class Achievements
{
    public static IReadOnlyList<Achievement> From(StatsSummary stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new[]
        {
            Make("first-round", L.T("app.achievement.first-round.title"), L.T("app.achievement.first-round.desc"), stats.Rounds >= 1, stats.Rounds + "/1"),
            Make("ten-rounds", L.T("app.achievement.ten-rounds.title"), L.T("app.achievement.ten-rounds.desc"), stats.Rounds >= 10, stats.Rounds + "/10"),
            Make("hundred-rounds", L.T("app.achievement.hundred-rounds.title"), L.T("app.achievement.hundred-rounds.desc"), stats.Rounds >= 100, stats.Rounds + "/100"),
            Make("first-win", L.T("app.achievement.first-win.title"), L.T("app.achievement.first-win.desc"), stats.Wins >= 1, stats.Wins + "/1"),
            Make("ten-wins", L.T("app.achievement.ten-wins.title"), L.T("app.achievement.ten-wins.desc"), stats.Wins >= 10, stats.Wins + "/10"),
            Make("hundred-wins", L.T("app.achievement.hundred-wins.title"), L.T("app.achievement.hundred-wins.desc"), stats.Wins >= 100, stats.Wins + "/100"),
            Make("first-tsumo", L.T("app.achievement.first-tsumo.title"), L.T("app.achievement.first-tsumo.desc"), stats.TsumoWins >= 1, stats.TsumoWins + "/1"),
            Make("ten-tsumo", L.T("app.achievement.ten-tsumo.title"), L.T("app.achievement.ten-tsumo.desc"), stats.TsumoWins >= 10, stats.TsumoWins + "/10"),
            Make("tiisuin", L.T("app.achievement.tiisuin.title"), L.T("app.achievement.tiisuin.desc"), stats.TiisuinCount >= 1, stats.TiisuinCount + "/1"),
            Make("witness", L.T("app.achievement.witness.title"), L.T("app.achievement.witness.desc"), stats.WitnessCount >= 1, stats.WitnessCount + "/1"),
        };
    }

    public static int UnlockedCount(IReadOnlyList<Achievement> achievements) =>
        achievements.Count(a => a.Unlocked);

    private static Achievement Make(string id, string title, string description, bool unlocked, string progress) =>
        new(id, title, description, progress, unlocked);
}
