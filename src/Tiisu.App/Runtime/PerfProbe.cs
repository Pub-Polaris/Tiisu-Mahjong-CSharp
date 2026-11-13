using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Tiisu.App.Runtime;

/// <summary>
/// 实际游戏进程内的轻量埋点（默认关闭）：
/// 用环境变量 <c>TIISU_PERF=1</c> 或 <c>%TEMP%\tiisu-perf-on</c> 标记文件启用。
/// 开启后只记录几个固定块，未开启时所有调用都是 no-op。
/// </summary>
public static class PerfProbe
{
    private const int Capacity = 512;
    private static readonly bool _enabled = DetectEnabled();
    private static readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private static readonly Stopwatch _logClock = Stopwatch.StartNew();
    private static readonly object _logGate = new();
    private static string? _logPath;

    public static bool Enabled => _enabled;

    public static Scope Measure(string name) => _enabled ? new Scope(name, Stopwatch.GetTimestamp()) : default;

    public readonly struct Scope : IDisposable
    {
        private readonly string? _name;
        private readonly long _start;
        private readonly long _allocStart;

        internal Scope(string name, long start)
        {
            _name = name;
            _start = start;
            _allocStart = GC.GetAllocatedBytesForCurrentThread();
        }

        public void Dispose()
        {
            if (_name is null)
            {
                return;
            }

            long ticks = Stopwatch.GetTimestamp() - _start;
            long alloc = GC.GetAllocatedBytesForCurrentThread() - _allocStart;
            _buckets.GetOrAdd(_name, _ => new Bucket()).Add(ticks, alloc);
        }
    }

    /// <summary>牌桌右上角 HUD 文案；关闭时为空。</summary>
    public static string HudText()
    {
        if (!_enabled)
        {
            return string.Empty;
        }

        double FrameMs() => Last("ui.frame");
        return "PERF  frame " + FrameMs().ToString("F2") + "ms  snap " + Last("snapshot.build").ToString("F2")
               + "ms  ai " + Last("ai.turn").ToString("F2") + "ms  alloc "
               + LastAlloc("ui.frame").ToString("F0") + "B";
    }

    /// <summary>每帧调用；每 5 秒把聚合结果追加到 %TEMP%\tiisu-perf.log。</summary>
    public static void Tick()
    {
        if (!_enabled || _logClock.Elapsed.TotalSeconds < 5)
        {
            return;
        }

        _logClock.Restart();
        Flush();
    }

    public static void Flush()
    {
        if (!_enabled)
        {
            return;
        }

        lock (_logGate)
        {
            try
            {
                _logPath ??= Path.Combine(Path.GetTempPath(), "tiisu-perf.log");
                var sb = new StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                foreach (var pair in _buckets.OrderByDescending(p => p.Value.TotalTicks))
                {
                    sb.AppendLine(pair.Value.Describe(pair.Key));
                }

                File.AppendAllText(_logPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // 性能日志失败不影响游戏
            }
        }
    }

    private static double Last(string name) =>
        _buckets.TryGetValue(name, out var bucket) ? bucket.LastMs : 0;

    private static double LastAlloc(string name) =>
        _buckets.TryGetValue(name, out var bucket) ? bucket.LastAlloc : 0;

    private static bool DetectEnabled()
    {
        try
        {
            if (string.Equals(Environment.GetEnvironmentVariable("TIISU_PERF"), "1", StringComparison.Ordinal))
            {
                return true;
            }

            return File.Exists(Path.Combine(Path.GetTempPath(), "tiisu-perf-on"));
        }
        catch
        {
            return false;
        }
    }

    private sealed class Bucket
    {
        private readonly object _gate = new();
        private readonly long[] _ticks = new long[Capacity];
        private readonly long[] _allocs = new long[Capacity];
        private int _count;
        private int _next;
        private long _totalTicks;
        private long _maxTicks;
        private long _lastTicks;
        private long _lastAlloc;

        public long TotalTicks
        {
            get
            {
                lock (_gate)
                {
                    return _totalTicks;
                }
            }
        }

        public double LastMs
        {
            get
            {
                lock (_gate)
                {
                    return _lastTicks * 1000.0 / Stopwatch.Frequency;
                }
            }
        }

        public double LastAlloc
        {
            get
            {
                lock (_gate)
                {
                    return _lastAlloc;
                }
            }
        }

        public void Add(long ticks, long alloc)
        {
            lock (_gate)
            {
                _count++;
                _totalTicks += ticks;
                _lastTicks = ticks;
                _lastAlloc = alloc;
                if (ticks > _maxTicks)
                {
                    _maxTicks = ticks;
                }

                _ticks[_next] = ticks;
                _allocs[_next] = alloc;
                _next = (_next + 1) % Capacity;
            }
        }

        public string Describe(string name)
        {
            long[] ticks;
            long total;
            long max;
            int count;
            lock (_gate)
            {
                ticks = (long[])_ticks.Clone();
                total = _totalTicks;
                max = _maxTicks;
                count = _count;
            }

            int used = Math.Min(count, Capacity);
            Array.Sort(ticks, 0, used);
            double ToMs(long t) => t * 1000.0 / Stopwatch.Frequency;
            double avg = count == 0 ? 0 : total * 1000.0 / Stopwatch.Frequency / count;
            double p50 = used == 0 ? 0 : ToMs(ticks[used / 2]);
            double p95 = used == 0 ? 0 : ToMs(ticks[Math.Min(used - 1, (int)(used * 0.95))]);
            return $"{name,-18} n={count,-7} avg={avg,8:F3}ms  p50={p50,8:F3}ms  p95={p95,8:F3}ms  max={ToMs(max),8:F3}ms";
        }
    }
}
