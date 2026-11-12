using System;
using System.Collections.Concurrent;
using System.Threading;
using Tiisu.App.Table;
using Tiisu.Engine.Random;

namespace Tiisu.App.Runtime;

/// <summary>
/// 对局会话：持 <see cref="MatchRuntime"/> + 一条专用后台线程。
///
/// 线程规则（与"纯 Skia、同进程直连"的改造一致）：
/// <list type="bullet">
/// <item>引擎 / 决策 / 日志全在后台线程，UI 线程一帧都不会被卡住；</item>
/// <item>命令只由 UI 线程 <see cref="Post"/> 投递（<see cref="BlockingCollection{T}"/> 逐条消费）；</item>
/// <item>事件通过构造时捕获的 <see cref="SynchronizationContext"/> Marshal 回 UI 线程；</item>
/// <item>UI 只读 <see cref="LatestSnapshot"/>（不可变快照），绝不触碰引擎对象。</item>
/// </list>
/// </summary>
public sealed class GameSession : IDisposable
{
    private readonly BlockingCollection<GameCommand> _queue = new(new ConcurrentQueue<GameCommand>());
    private readonly Thread _worker;
    private readonly SynchronizationContext? _uiContext;
    private readonly MatchRuntime _runtime;

    private TableViewModel _latest;
    private string? _lastError;
    private volatile bool _disposed;

    /// <summary>创建会话。必须在 UI 线程构造（用当前 <see cref="SynchronizationContext"/> 回投事件）。</summary>
    /// <param name="settings">初始设置（读自 settings.json）。</param>
    /// <param name="dataDirectory">数据目录；null = <c>%APPDATA%\TiisuMahjong</c>。</param>
    /// <param name="uiContext">事件回投的同步上下文；null = 当前线程的上下文。</param>
    /// <param name="random">随机源（测试可注入 seed）。</param>
    /// <param name="useStores">是否读写磁盘（统计 / 日志）；无窗口自检也用真目录。对局存档已整体停用，不再建 <see cref="SaveStore"/>。</param>
    /// <param name="aiStepDelayMilliseconds">每个 AI 回合后的停顿；0 = 全速（自检用）。</param>
    public GameSession(
        Settings settings,
        string? dataDirectory = null,
        SynchronizationContext? uiContext = null,
        IRandomSource? random = null,
        bool useStores = true,
        int aiStepDelayMilliseconds = 120)
    {
        settings ??= new Settings();
        _uiContext = uiContext ?? SynchronizationContext.Current;

        string directory = dataDirectory ?? Settings.DefaultDirectory;

        // 「存档 / 恢复上局」已整体停用：不建 SaveStore，state.json 不读也不写。
        // 统计与牌谱日志不受影响。
        StatsStore? stats = null;
        RoundLogWriter? log = null;
        if (useStores)
        {
            stats = new StatsStore(directory);
            log = new RoundLogWriter(directory) { Enabled = settings.RecordTiles };
        }

        _runtime = new MatchRuntime(settings, random ?? new SystemRandomSource(), Dispatch,
            saveStore: null, stats: stats, roundLog: log)
        {
            AiStepDelayMilliseconds = aiStepDelayMilliseconds,
        };

        _latest = _runtime.Snapshot;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Tiisu.GameSession",
        };
        _worker.Start();
    }

    /// <summary>UI 每帧只读这个快照。</summary>
    public TableViewModel LatestSnapshot => Volatile.Read(ref _latest);

    /// <summary>运行阶段（只读枚举，供 UI 决定是否画"下一局"等提示）。</summary>
    public MatchPhase Phase => _runtime.Phase;

    /// <summary>最近一次错误（UI 可显示）。</summary>
    public string? LastError => Volatile.Read(ref _lastError);

    /// <summary>事件（已 Marshal 到 UI 线程）。</summary>
    public event Action<UiEvent>? UiEvent;

    /// <summary>投递一条命令（只允许 UI 线程调用）。</summary>
    public void Post(GameCommand command)
    {
        if (command is null || _disposed)
        {
            return;
        }

        // 互动命令带上 UI 当前看到的快照版本；引擎收到时版本对不上就拒绝，避免按到旧画面。
        if (command.SnapshotRevision == 0
            && command.Kind is not (GameCommandKind.StartMatch or GameCommandKind.ResetAll
                or GameCommandKind.ApplySettings or GameCommandKind.Resume))
        {
            command = command with { SnapshotRevision = LatestSnapshot.Revision };
        }

        try
        {
            _queue.Add(command);
        }
        catch (InvalidOperationException)
        {
            // 队列已关闭（正在 Dispose）。
        }
    }

    private void WorkerLoop()
    {
        // 后台线程上的未处理异常 = 进程级崩溃（P0-2）：整个循环兜底，
        // 队列在 Dispose 超时后被释放时，MoveNext 抛的 ObjectDisposedException 必须吞掉。
        try
        {
            foreach (var command in _queue.GetConsumingEnumerable())
            {
                try
                {
                    _runtime.Execute(command);
                }
                catch (Exception ex)
                {
                    Dispatch(new ErrorEvent(ex.Message));
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // 会话已释放，正常退出。
        }
        catch (InvalidOperationException)
        {
            // 队列已关闭。
        }
        catch (Exception ex)
        {
            try
            {
                Dispatch(new ErrorEvent(ex.Message));
            }
            catch
            {
                // 事件通道也失效时只能静默退出
            }
        }
    }

    private void Dispatch(UiEvent uiEvent)
    {
        if (uiEvent is StateChangedEvent changed)
        {
            Volatile.Write(ref _latest, changed.Snapshot);
        }

        if (uiEvent is ErrorEvent error)
        {
            Volatile.Write(ref _lastError, error.Message);
        }

        var handler = UiEvent;
        if (handler is null)
        {
            return;
        }

        if (_uiContext is null)
        {
            handler(uiEvent);
            return;
        }

        try
        {
            _uiContext.Post(_ => handler(uiEvent), null);
        }
        catch (InvalidOperationException)
        {
            // UI 线程已退出（窗口关闭中）。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        bool joined;
        try
        {
            // 等引擎线程收尾；超时就不释放队列——宁可泄漏一点内存，也不要
            // 让后台线程在 MoveNext 上抛未处理异常把进程带走（P0-2）。
            joined = _worker.Join(5000);
        }
        catch (ThreadStateException)
        {
            // 线程尚未启动（构造中途失败）。
            joined = true;
        }

        if (joined)
        {
            try
            {
                _queue.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 已释放
            }
        }
        else
        {
            // 超时：泄漏换不崩。至少留一条日志，别让线上表现为「内存缓慢上涨 + 无线索」。
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tiisu-error.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff")
                    + " GameSession.Dispose: 引擎线程 5s 未退出，放弃释放命令队列（P0-2 临时方案）"
                    + Environment.NewLine);
            }
            catch
            {
                // 日志失败忽略
            }
        }
    }

    private sealed class SystemRandomSource : IRandomSource
    {
        public double NextDouble() => Random.Shared.NextDouble();
    }
}
