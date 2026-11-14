using System;
using System.Collections.Generic;
using System.Linq;

namespace Tiisu.App.Runtime;

public sealed record SteamAchievement(string LocalId, string SteamId, string Name, bool Hidden);

/// <summary>
/// 本地成就 → Steam Achievement 的稳定映射。Steamworks 接入前也可被预检工具使用。
/// 本地 ID 定义在 <see cref="Achievements"/>；改本地 ID 会破坏已上传的成就。
/// </summary>
public static class SteamAchievementMap
{
    public static readonly IReadOnlyList<SteamAchievement> Items = new[]
    {
        new SteamAchievement("first-round", "ACH_FIRST_ROUND", "初战", false),
        new SteamAchievement("ten-rounds", "ACH_TEN_ROUNDS", "十局", false),
        new SteamAchievement("hundred-rounds", "ACH_HUNDRED_ROUNDS", "百局", false),
        new SteamAchievement("first-win", "ACH_FIRST_WIN", "首胜", false),
        new SteamAchievement("ten-wins", "ACH_TEN_WINS", "十胜", false),
        new SteamAchievement("hundred-wins", "ACH_HUNDRED_WINS", "百胜", false),
        new SteamAchievement("first-tsumo", "ACH_FIRST_TSUMO", "自摸", false),
        new SteamAchievement("ten-tsumo", "ACH_TEN_TSUMO", "自摸十次", false),
        new SteamAchievement("tiisuin", "ACH_TIISUIN", "大七星", true),
        new SteamAchievement("witness", "ACH_WITNESS", "见证", true),
    };

    public static string? SteamIdFor(string localId) =>
        Items.FirstOrDefault(x => string.Equals(x.LocalId, localId, StringComparison.Ordinal))?.SteamId;

    public static IReadOnlyList<string> SteamIdsFor(IEnumerable<string> localIds) =>
        localIds.Select(SteamIdFor).Where(id => id is not null).Select(id => id!).Distinct().ToArray();
}
