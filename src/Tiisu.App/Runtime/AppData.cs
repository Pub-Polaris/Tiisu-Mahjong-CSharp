using System;
using System.IO;
using System.Threading;

namespace Tiisu.App.Runtime;

/// <summary>
/// 本机数据目录。<see cref="Root"/> 是旧版平铺目录（迁移与共享缓存用），
/// <see cref="Current"/> 是当前本地玩家档案目录；所有按档案隔离的 store 默认读它。
/// </summary>
public static class AppData
{
    /// <summary><c>%APPDATA%\TiisuMahjong</c>；不随档案切换。</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TiisuMahjong");

    private static string _current = Root;

    /// <summary>当前档案目录；档案切换后由 UI 线程更新。</summary>
    public static string Current => Volatile.Read(ref _current);

    /// <summary>切到某个档案目录（只改本进程；调用方负责在重建屏幕前完成）。</summary>
    public static void Use(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("directory is required", nameof(directory));
        }

        string full = Path.GetFullPath(directory);
        Directory.CreateDirectory(full);
        Volatile.Write(ref _current, full);
    }

    /// <summary>显式目录优先；否则当前档案目录。</summary>
    public static string Resolve(string? directory) =>
        string.IsNullOrWhiteSpace(directory) ? Current : Path.GetFullPath(directory);
}
