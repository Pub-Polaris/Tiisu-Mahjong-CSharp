using System;
using Tiisu.Localization;
using System.Diagnostics;
using System.IO;

namespace Tiisu.App.Table;

/// <summary>
/// 皮肤仓库：决定这次运行用哪张皮，并保证「没有皮肤 = 和原来逐像素一致」。
///
/// 解析顺序：
///   1. 环境变量 <c>TIISU_SKIN</c>：<c>off / none / 0</c> 强制程序化（做对照、排障）；
///      否则当作 <c>skin.json</c> 或包含它的目录（预览、开发时指向 <c>art/table/…</c>）。
///   2. <c>&lt;Assets&gt;/ui/skin.json</c>（美术把文件放这里即随包启用）。
///   3. 都没有 → <see cref="TableSkin.None"/>：绘制走原来的 FillRoundRect / DrawText 路径。
///
/// 加载失败只记一行警告并回退；牌桌不会因为一张坏皮打不开。
/// </summary>
public static class SkinStore
{
    public const string EnvVar = "TIISU_SKIN";

    private static readonly object Gate = new();
    private static TableSkin? _current;
    private static bool _resolved;
    private static string? _lastWarning;

    /// <summary>当前皮肤；没有可用皮肤时是 <see cref="TableSkin.None"/>（永不抛异常）。</summary>
    public static TableSkin Current
    {
        get
        {
            lock (Gate)
            {
                if (!_resolved)
                {
                    _resolved = true;
                    _current = Resolve();
                }

                return _current ?? TableSkin.None;
            }
        }
    }

    /// <summary>true = 正在走程序化绘制（与旧版视觉一致）。</summary>
    public static bool IsFallback => !Current.IsActive;

    /// <summary>最近一次加载失败的说明（设置页/自检可读）。</summary>
    public static string? LastWarning
    {
        get
        {
            lock (Gate)
            {
                return _lastWarning;
            }
        }
    }

    /// <summary>强制重新解析（设置页换皮 / 开发热重载）。旧皮释放，静态层缓存同时清空。</summary>
    public static void Reload()
    {
        lock (Gate)
        {
            TableSkin? old = _current;
            _current = null;
            _resolved = false;
            _lastWarning = null;
            if (old is not null && !ReferenceEquals(old, TableSkin.None))
            {
                old.Dispose();
            }
        }

        // 缓存的画面引用旧皮的 SKImage，必须先丢。
        TableSceneSkia.ClearStaticLayers();
    }

    /// <summary>自检 / 预览注入：不读文件，直接用给定皮肤（null = 程序化）。</summary>
    public static void SetForTests(TableSkin? skin)
    {
        lock (Gate)
        {
            TableSkin? old = _current;
            _current = skin ?? TableSkin.None;
            _resolved = true;
            if (old is not null && !ReferenceEquals(old, TableSkin.None) && !ReferenceEquals(old, skin))
            {
                old.Dispose();
            }
        }

        TableSceneSkia.ClearStaticLayers();
    }

    /// <summary>本进程会尝试加载的清单路径；没有则 null（没被环境变量强制关闭时也返回 null）。</summary>
    public static string? ResolvePath()
    {
        string? env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            string value = env.Trim();
            if (IsOff(value))
            {
                return null;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(value);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Warn(L.T("app.table.skin.pathInvalid", ("env", EnvVar), ("value", value), ("message", ex.Message)));
                return null;
            }

            if (Directory.Exists(candidate))
            {
                candidate = Path.Combine(candidate, TableSkin.FileName);
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }

            Warn(L.T("app.table.skin.notExist", ("env", EnvVar), ("path", candidate)));
            return null;
        }

        string shipped = Path.Combine(AssetsUiDirectory(), TableSkin.FileName);
        return File.Exists(shipped) ? shipped : null;
    }

    private static TableSkin Resolve()
    {
        string? path = ResolvePath();
        if (path is null)
        {
            return TableSkin.None;
        }

        TableSkin? skin = TableSkin.Load(path, out string? error);
        if (skin is null)
        {
            Warn(L.T("app.table.skin.loadFailed", ("path", path), ("error", error)));
            return TableSkin.None;
        }

        return skin;
    }

    private static string AssetsUiDirectory() =>
        Path.Combine(Tiisu.App.Program.AssetsDir, "ui");

    private static bool IsOff(string value) =>
        value.ToLowerInvariant() is "off" or "none" or "0" or "false";

    private static void Warn(string message)
    {
        _lastWarning = message;
        Debug.WriteLine("[skin] " + message);
        try
        {
            Console.Error.WriteLine("[skin] " + message);
        }
        catch (IOException)
        {
            // 无控制台：忽略。
        }
    }
}
