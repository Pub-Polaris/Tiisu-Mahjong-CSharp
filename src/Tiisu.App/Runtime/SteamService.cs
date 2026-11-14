using System;
using System.Collections.Generic;

namespace Tiisu.App.Runtime;

/// <summary>
/// Steam 服务抽象。没有 SDK 时用 <see cref="NullSteamService"/>，调用方不需要判空。
/// 真实实现只允许在 Steamworks 接入后添加，业务层不直接引用 SDK。
/// </summary>
public interface ISteamService
{
    bool IsAvailable { get; }
    bool TryInitialize();
    void Shutdown();
    bool UnlockAchievement(string steamId);
    bool StoreStat(string statId, int value);
}

/// <summary>离线/无 AppID 时的空实现：所有调用 no-op，不阻塞游戏。</summary>
public sealed class NullSteamService : ISteamService
{
    public bool IsAvailable => false;
    public bool TryInitialize() => false;
    public void Shutdown() { }
    public bool UnlockAchievement(string steamId) => false;
    public bool StoreStat(string statId, int value) => false;
}

/// <summary>
/// 把本地成就批量上报到 Steam。幂等：同一 Steam ID 只上报一次。
/// 真实离线队列留到 SDK 接入时再落盘；本轮先锁行为。
/// </summary>
public sealed class SteamAchievementSync
{
    private readonly ISteamService _steam;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    public SteamAchievementSync(ISteamService steam)
    {
        _steam = steam ?? throw new ArgumentNullException(nameof(steam));
    }

    public IReadOnlyList<string> Reported => new List<string>(_reported);

    public IReadOnlyList<string> Report(IEnumerable<Achievement> achievements)
    {
        ArgumentNullException.ThrowIfNull(achievements);
        if (!_steam.IsAvailable)
        {
            return Array.Empty<string>();
        }

        var sent = new List<string>();
        foreach (Achievement achievement in achievements)
        {
            if (!achievement.Unlocked)
            {
                continue;
            }

            string? steamId = SteamAchievementMap.SteamIdFor(achievement.Id);
            if (steamId is null || _reported.Contains(steamId))
            {
                continue;
            }

            if (_steam.UnlockAchievement(steamId))
            {
                _reported.Add(steamId);
                sent.Add(steamId);
            }
        }

        return sent;
    }
}
