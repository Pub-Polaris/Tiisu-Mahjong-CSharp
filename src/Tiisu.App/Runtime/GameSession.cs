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
/// <item>引擎 / 决策 / 存档 / 日志全在后台线程，UI 线程一帧都不会被卡住；</item>
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
    private readonly SaveStore? _saveStore;

    private TableViewModel _latest;
    private string? _lastError;
    private volatile bool _disposed;

    /// <summary>创建会话。必须在 UI 线程构造（用当前 <see cref="SynchronizationContext"/> 回投事件）。</summary>
    /// <param name="settings">初始设置（读自 settings.json）。</param>
    /// <param name="dataDirectory">数据目录；null = <c>%APPDATA%\TiisuMahjong</c>。</param>
    /// <param name="uiContext">事件回投的同步上下文；null = 当前线程的上下文。</param>
    /// <param name="random">随机源（测试可注入 seed）。</param>
    /// <param name="useStores">是否读写磁盘（存档 / 统计 / 日志）；无窗口自检也用真目录。</param>
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
        SaveStore? save = null;
        StatsStore? stats = null;
        RoundLogWriter? log = null;
        if (useStores)
        {
            save = new SaveStore(directory);
            stats = new StatsStore(directory);
            log = new RoundLogWriter(directory) { Enabled = settings.RecordTiles };
            _saveStore = save;
        }

        _runtime = new MatchRuntime(settings, random ?? new SystemRandomSource(), Dispatch, save, stats, log)
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

        try
        {
            _queue.Add(command);
        }
        catch (InvalidOperationException)
        {
            // 队列已关闭（正在 Dispose）。
        }
    }

    /// <summary>读磁盘存档并投递 Resume；无可恢复存档返回 false。</summary>
    public bool PostResume()
    {
        var save = _saveStore?.Load();
        if (save is null || !save.InProgress)
        {
            return false;
        }

        Post(GameCommand.Resume(save));
        return true;
    }

    /// <summary>存档文件路径（UI / 自检打印用）。</summary>
    public string? StatePath => _saveStore?.StatePath;

    private void WorkerLoop()
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
        _queue.CompleteAdding();
        try
        {
            _worker.Join(2000);
        }
        catch (ThreadStateException)
        {
            // 线程尚未启动（构造中途失败）。
        }

        _saveStore?.Dispose();
        _queue.Dispose();
    }

    private sealed class SystemRandomSource : IRandomSource
    {
        public double NextDouble() => Random.Shared.NextDouble();
    }
}
