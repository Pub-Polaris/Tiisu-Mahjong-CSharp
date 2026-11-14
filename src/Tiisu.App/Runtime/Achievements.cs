using System;
using System.Collections.Generic;
using System.Linq;
using Tiisu.App.Runtime;

namespace Tiisu.App.Runtime;

public sealed record Achievement(string Id, string Title, string Description, bool Unlocked);

/// <summary>从本机统计派生成就；不单独持久化，规则变化时可直接重算。</summary>
public static class Achievements
{
    public static IReadOnlyList<Achievement> From(StatsSummary stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new[]
        {
            Make("first-round", "初战", "完成 1 个小局", stats.Rounds >= 1, "局数 " + stats.Rounds + "/1"),
            Make("ten-rounds", "十局", "完成 10 个小局", stats.Rounds >= 10, "局数 " + stats.Rounds + "/10"),
            Make("hundred-rounds", "百局", "完成 100 个小局", stats.Rounds >= 100, "局数 " + stats.Rounds + "/100"),
            Make("first-win", "首胜", "和牌 1 次", stats.Wins >= 1, "和牌 " + stats.Wins + "/1"),
            Make("ten-wins", "十胜", "和牌 10 次", stats.Wins >= 10, "和牌 " + stats.Wins + "/10"),
            Make("hundred-wins", "百胜", "和牌 100 次", stats.Wins >= 100, "和牌 " + stats.Wins + "/100"),
            Make("first-tsumo", "自摸", "自摸 1 次", stats.TsumoWins >= 1, "自摸 " + stats.TsumoWins + "/1"),
            Make("ten-tsumo", "自摸十次", "自摸 10 次", stats.TsumoWins >= 10, "自摸 " + stats.TsumoWins + "/10"),
            Make("tiisuin", "大七星", "打出一次大七星", stats.TiisuinCount >= 1, "大七星 " + stats.TiisuinCount + "/1"),
            Make("witness", "见证", "见证一次大七星", stats.WitnessCount >= 1, "见证 " + stats.WitnessCount + "/1"),
        };
    }

    public static int UnlockedCount(IReadOnlyList<Achievement> achievements) =>
        achievements.Count(a => a.Unlocked);

    private static Achievement Make(string id, string title, string description, bool unlocked, string progress) =>
        new(id, title, unlocked ? description : description + "（" + progress + "）", unlocked);
}
