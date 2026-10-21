using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Tiisu.App.Runtime;

/// <summary>
/// 会话存档（<c>%APPDATA%\TiisuMahjong\state.json</c>）。
/// 字段名与 Web 侧 <c>engine.js saveSessionState()</c> 完全一致，旧 JS 存档可直接读、也能被旧代码读回。
/// 写入按 200ms 节流：调用方每步都可 <see cref="Save"/>，真正落盘由节流器合并。
/// </summary>
public sealed class SaveStore : IDisposable
{
    public const int ThrottleMilliseconds = 200;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;
    private SaveState? _pending;
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private bool _disposed;

    public SaveStore(string? directory = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? Settings.DefaultDirectory);
        Directory.CreateDirectory(DirectoryPath);
        _timer = new System.Threading.Timer(_ => FlushPending(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    public string DirectoryPath { get; }

    public string StatePath => Path.Combine(DirectoryPath, "state.json");

    /// <summary>读存档；不存在 / 损坏返回 null（调用方按"无存档"处理）。</summary>
    public SaveState? Load()
    {
        if (!File.Exists(StatePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SaveState>(File.ReadAllText(StatePath, Encoding.UTF8), ReadOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>节流写入：距上次落盘 ≥200ms 立即写，否则合并到 200ms 窗口末尾。</summary>
    public void Save(SaveState state)
    {
        if (state is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastWriteUtc).TotalMilliseconds >= ThrottleMilliseconds)
            {
                WriteCore(state);
                return;
            }

            _pending = state;
            int delay = Math.Max(1, ThrottleMilliseconds - (int)(now - _lastWriteUtc).TotalMilliseconds);
            _timer.Change(delay, System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>立刻落盘（终局 / 存档命令用，绕过节流）。</summary>
    public void SaveNow(SaveState state)
    {
        if (state is null)
        {
            return;
        }

        lock (_gate)
        {
            _pending = null;
            WriteCore(state);
        }
    }

    /// <summary>把挂起的写入立即落盘。</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_pending is { } pending)
            {
                _pending = null;
                WriteCore(pending);
            }
        }
    }

    private void FlushPending()
    {
        lock (_gate)
        {
            if (_disposed || _pending is not { } pending)
            {
                return;
            }

            _pending = null;
            WriteCore(pending);
        }
    }

    private void WriteCore(SaveState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        try
        {
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, WriteOptions), new UTF8Encoding(false));
            _lastWriteUtc = DateTime.UtcNow;
        }
        catch (IOException)
        {
            // 磁盘抖动时保留 pending，下一次 Save 会重试。
            _pending = state;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending is { } pending)
            {
                _pending = null;
                WriteCore(pending);
            }

            _disposed = true;
            _timer.Dispose();
        }
    }
}

/// <summary>存档内容（字段与 Web 侧 <c>state.json</c> 同名）。</summary>
public sealed class SaveState
{
    [JsonPropertyName("session")]
    public string Session { get; set; } = string.Empty;

    [JsonPropertyName("savedAt")]
    public string SavedAt { get; set; } = string.Empty;

    [JsonPropertyName("playerUUID")]
    public string PlayerUUID { get; set; } = string.Empty;

    [JsonPropertyName("modeTarget")]
    public int ModeTarget { get; set; } = 1;

    [JsonPropertyName("handsPlayed")]
    public int HandsPlayed { get; set; }

    [JsonPropertyName("handNumber")]
    public int HandNumber { get; set; }

    [JsonPropertyName("scores")]
    public int[] Scores { get; set; } = Array.Empty<int>();

    [JsonPropertyName("dealerIndex")]
    public int DealerIndex { get; set; }

    [JsonPropertyName("honba")]
    public int Honba { get; set; }

    [JsonPropertyName("roundWindIdx")]
    public int RoundWindIdx { get; set; }

    [JsonPropertyName("dealerCount")]
    public int DealerCount { get; set; }

    [JsonPropertyName("inProgress")]
    public bool InProgress { get; set; }
}
