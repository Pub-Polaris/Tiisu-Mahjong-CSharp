using System;
using System.Collections.Generic;
using System.Linq;

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
/// 服务不可用时把 Steam ID 写入离线队列；服务恢复后补报。
/// </summary>
public sealed class SteamAchievementSync
{
    private readonly ISteamService _steam;
    private readonly SteamAchievementStore? _store;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    public SteamAchievementSync(ISteamService steam, SteamAchievementStore? store = null)
    {
        _steam = steam ?? throw new ArgumentNullException(nameof(steam));
        _store = store;
    }

    public IReadOnlyList<string> Reported => new List<string>(_reported);

    public IReadOnlyList<string> Report(IEnumerable<Achievement> achievements)
    {
        ArgumentNullException.ThrowIfNull(achievements);
        var unlocked = achievements
            .Where(a => a.Unlocked)
            .Select(a => SteamAchievementMap.SteamIdFor(a.Id))
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!_steam.IsAvailable)
        {
            _store?.Add(unlocked);
            return Array.Empty<string>();
        }

        var sent = new List<string>();
        var pending = _store?.Load() ?? Array.Empty<string>();
        foreach (string steamId in pending.Concat(unlocked).Distinct(StringComparer.Ordinal))
        {
            if (_reported.Contains(steamId))
            {
                continue;
            }

            if (_steam.UnlockAchievement(steamId))
            {
                _reported.Add(steamId);
                sent.Add(steamId);
            }
        }

        if (sent.Count > 0)
        {
            _store?.Remove(sent);
        }

        return sent;
    }
}
