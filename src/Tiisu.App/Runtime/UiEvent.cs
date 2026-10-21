using Tiisu.App.Table;

namespace Tiisu.App.Runtime;

/// <summary>引擎线程 → UI 线程的事件。全部在 UI 线程（<see cref="GameSession"/> 的同步上下文）上派发。</summary>
public abstract record UiEvent;

/// <summary>一步之后的不可变快照（UI 只读这个）。</summary>
public sealed record StateChangedEvent(TableViewModel Snapshot) : UiEvent;

/// <summary>提示消息（和牌 / 流局 / 恢复存档等）。<paramref name="Important"/> 对应 Web 侧 10 秒自动消失。</summary>
public sealed record MessageEvent(string Text, bool Important) : UiEvent;

/// <summary>一局结束（连庄 / 下庄已由引擎结算）。</summary>
public sealed record RoundEndEvent(RoundResult Result) : UiEvent;

/// <summary>整场结束（大七星 / 出岭 / 终局）。</summary>
public sealed record MatchOverEvent(string Reason) : UiEvent;

/// <summary>错误：命令被拒绝或内部异常。</summary>
public sealed record ErrorEvent(string Message) : UiEvent;
