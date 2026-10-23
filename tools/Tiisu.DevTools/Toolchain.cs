using System.Globalization;

namespace Tiisu.DevTools;

/// <summary>
/// 仓库根与外部工具（<c>Tiisu.Mahjong.exe</c> / <c>Tiisu.AudioGen.exe</c>）的位置。
///
/// 仓库根：<c>TIISU_ROOT</c> 环境变量 → 从本程序目录 / 当前目录逐级向上找
/// 「含 <c>src/Tiisu.App</c> 与 <c>Tiisu.slnx</c>」的目录。
///
/// 可执行文件：先试已知的构建输出（Release 优先、再 Debug），都没有就在 <c>bin/</c> 下递归找最新的一个。
/// </summary>
internal sealed class Toolchain
{
    private const string GameExeName = "Tiisu.Mahjong.exe";

    private const string AudioGenExeName = "Tiisu.AudioGen.exe";

    private static readonly string[] GameCandidates =
    {
        "src/Tiisu.App/bin/Release/net10.0-windows10.0.19041/Tiisu.Mahjong.exe",
        "src/Tiisu.App/bin/Release/net10.0-windows10.0.19041/win-x64/Tiisu.Mahjong.exe",
        "src/Tiisu.App/bin/Release/net10.0-windows/Tiisu.Mahjong.exe",
        "src/Tiisu.App/bin/Debug/net10.0-windows10.0.19041/Tiisu.Mahjong.exe",
        "src/Tiisu.App/bin/Debug/net10.0-windows10.0.19041/win-x64/Tiisu.Mahjong.exe",
        "src/Tiisu.App/bin/Debug/net10.0-windows/Tiisu.Mahjong.exe",
    };

    private static readonly string[] AudioGenCandidates =
    {
        "tools/Tiisu.AudioGen/bin/Release/net10.0-windows/Tiisu.AudioGen.exe",
        "tools/Tiisu.AudioGen/bin/Release/net10.0/Tiisu.AudioGen.exe",
        "tools/Tiisu.AudioGen/bin/Debug/net10.0-windows/Tiisu.AudioGen.exe",
        "tools/Tiisu.AudioGen/bin/Debug/net10.0/Tiisu.AudioGen.exe",
    };

    private Toolchain(string? root, string? gameExe, string? audioGenExe, IReadOnlyList<string> notes)
    {
        Root = root;
        GameExe = gameExe;
        AudioGenExe = audioGenExe;
        Notes = notes;
    }

    /// <summary>仓库根（<c>NET</c> 目录）；没找到时为 null。</summary>
    public string? Root { get; }

    public string? GameExe { get; }

    public string? AudioGenExe { get; }

    /// <summary>路径解析过程，启动时写进日志，找不到工具时可以据此排错。</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>没有仓库根时退回当前目录，至少让「打开布局文件」这类不依赖构建产物的操作可用。</summary>
    public string WorkingDirectory => Root ?? Directory.GetCurrentDirectory();

    public string OutputDir => Path.Combine(WorkingDirectory, "artifacts", "uitools");

    public string AssetsDir => Path.Combine(WorkingDirectory, "src", "Tiisu.App", "Assets");

    public string SoundsDir => Path.Combine(AssetsDir, "sounds");

    public string VoicesDir => Path.Combine(AssetsDir, "voices");

    public string LayoutFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TiisuMahjong",
        "layout-custom.json");

    public static Toolchain Discover()
    {
        var notes = new List<string>();

        string? root = NormalizeRoot(Environment.GetEnvironmentVariable("TIISU_ROOT"), notes);
        if (root is not null)
        {
            notes.Add("仓库根来自 TIISU_ROOT：" + root);
        }

        root ??= FindRoot(AppContext.BaseDirectory) ?? FindRoot(Directory.GetCurrentDirectory());
        if (root is null)
        {
            notes.Add("未找到仓库根（向上找不到同时含 src/Tiisu.App 与 Tiisu.slnx 的目录）；可用环境变量 TIISU_ROOT 指定。");
        }

        string? gameExe = null;
        string? audioGenExe = null;
        if (root is not null)
        {
            gameExe = FindExecutable(
                root,
                GameCandidates,
                Path.Combine(root, "src", "Tiisu.App", "bin"),
                GameExeName);
            audioGenExe = FindExecutable(
                root,
                AudioGenCandidates,
                Path.Combine(root, "tools", "Tiisu.AudioGen", "bin"),
                AudioGenExeName);
        }

        notes.Add(GameExeName + "：" + (gameExe ?? "未找到（先 dotnet build src/Tiisu.App）"));
        notes.Add(AudioGenExeName + "：" + (audioGenExe ?? "未找到（先 dotnet build tools/Tiisu.AudioGen）"));

        return new Toolchain(root, gameExe, audioGenExe, notes);
    }

    /// <summary>游戏工作目录 = 可执行文件所在目录（Assets 与 exe 同级）。</summary>
    public string GameWorkingDirectory =>
        GameExe is null ? WorkingDirectory : Path.GetDirectoryName(GameExe) ?? WorkingDirectory;

    private static string? NormalizeRoot(string? value, List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string path = value.Trim().Trim('"').Trim();
        if (!Directory.Exists(path))
        {
            notes.Add("TIISU_ROOT 指向的目录不存在：" + path);
            return null;
        }

        path = Path.GetFullPath(path);

        // 允许指向仓库外层（TiisuMahjong），自动下探一层 NET。
        string nested = Path.Combine(path, "NET");
        if (!LooksLikeRoot(path) && LooksLikeRoot(nested))
        {
            return nested;
        }

        if (!LooksLikeRoot(path))
        {
            notes.Add("TIISU_ROOT 目录里没有 src/Tiisu.App 与 Tiisu.slnx，仍按原样使用：" + path);
        }

        return path;
    }

    private static bool LooksLikeRoot(string dir) =>
        Directory.Exists(Path.Combine(dir, "src", "Tiisu.App")) &&
        File.Exists(Path.Combine(dir, "Tiisu.slnx"));

    private static string? FindRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            if (LooksLikeRoot(dir.FullName))
            {
                return dir.FullName;
            }

            string nested = Path.Combine(dir.FullName, "NET");
            if (LooksLikeRoot(nested))
            {
                return nested;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindExecutable(
        string root,
        IReadOnlyList<string> candidates,
        string binDir,
        string fileName)
    {
        foreach (string relative in candidates)
        {
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                return full;
            }
        }

        if (!Directory.Exists(binDir))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(binDir, fileName, SearchOption.AllDirectories)
                .OrderByDescending(path => path.Contains(
                    Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => new FileInfo(path).LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
