using System;

namespace Tiisu.App.Runtime;

/// <summary>档案来源：本地创建，或未来 Steam 身份自动映射。</summary>
public enum ProfileSource
{
    Local,
    Steam,
}

/// <summary>
/// 一个本地玩家档案。目录只由 <see cref="Id"/> 派生，显示名可随时改；
/// Steam 身份只在未来接入时写入 <see cref="SteamId"/>。
/// </summary>
public sealed record PlayerProfile(
    string Id,
    string DisplayName,
    string Directory,
    DateTimeOffset CreatedAt,
    string? SteamId = null,
    ProfileSource Source = ProfileSource.Local)
{
    public bool IsSteam => Source == ProfileSource.Steam || !string.IsNullOrWhiteSpace(SteamId);
}
