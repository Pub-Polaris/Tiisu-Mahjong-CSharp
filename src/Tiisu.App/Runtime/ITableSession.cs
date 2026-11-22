using Tiisu.App.Table;

namespace Tiisu.App.Runtime;

/// <summary>
/// 牌桌会话抽象。单机由 <see cref="GameSession"/> 实现（本地引擎）；
/// 联机客户端由网络快照实现，不接触 Tiisu.Engine。
/// </summary>
public interface ITableSession : IDisposable
{
    TableViewModel LatestSnapshot { get; }

    MatchPhase Phase { get; }

    string? LastError { get; }

    event Action<UiEvent>? UiEvent;

    void Post(GameCommand command);
}
