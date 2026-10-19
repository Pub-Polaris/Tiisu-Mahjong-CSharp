namespace Tiisu.Shell;

/// <summary>命令行开关。</summary>
internal sealed class ShellOptions
{
    public string? WebDirectory { get; private set; }

    /// <summary>自检秒数（从页面加载完成算起）；0 表示不自检。</summary>
    public int SmokeSeconds { get; private set; }

    public string? ShotPath { get; private set; }

    /// <summary>截图前等待毫秒数（默认 3000）。</summary>
    public int ShotDelayMs { get; private set; } = 3000;

    /// <summary>开发/调试：不附加 auto=1，用人类对局模式加载页面。</summary>
    public bool NoAuto { get; private set; }

    /// <summary>开发/调试：人类对局模式下自动点第一张手牌出牌（用于无头验证人类桥）。</summary>
    public bool DriveHuman { get; private set; }

    /// <summary>指定要加载的页面（相对 Web 根目录，可带查询串）；默认门户页。</summary>
    public string? OpenPage { get; private set; }

    /// <summary>自检 / 截图时自动开局的目标局数（等价于 index.html?auto=1&amp;mode=N）。</summary>
    public int? AutoMode { get; private set; }

    public bool ShowHelp { get; private set; }

    public bool IsHeadless => SmokeSeconds > 0 || ShotPath is not null;

    public static ShellOptions Parse(string[] args)
    {
        var options = new ShellOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--web":
                    if (i + 1 < args.Length)
                    {
                        options.WebDirectory = args[++i];
                    }

                    break;

                case "--smoke":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int seconds))
                    {
                        options.SmokeSeconds = Math.Max(0, seconds);
                        i++;
                    }
                    else
                    {
                        options.SmokeSeconds = 15;
                    }

                    break;

                case "--shot":
                    if (i + 1 < args.Length)
                    {
                        options.ShotPath = args[++i];
                    }

                    break;

                case "--delay":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int delayMs))
                    {
                        options.ShotDelayMs = Math.Max(0, delayMs);
                        i++;
                    }

                    break;

                case "--no-auto":
                    options.NoAuto = true;
                    break;

                case "--drive-human":
                    options.DriveHuman = true;
                    options.NoAuto = true;
                    break;

                case "--open":
                    if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        options.OpenPage = args[++i].Trim();
                    }

                    break;

                case "--auto":
                case "--auto-start":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int mode))
                    {
                        options.AutoMode = Math.Max(1, mode);
                        i++;
                    }
                    else
                    {
                        options.AutoMode = 1;
                    }

                    break;

                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;
            }
        }

        return options;
    }
}

/// <summary>从 <see cref="AppContext.BaseDirectory"/> 往上找 <c>Web/index.html</c>。</summary>
internal static class WebDirectoryLocator
{
    public static string? Resolve(string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            var full = Path.GetFullPath(explicitDirectory);
            if (File.Exists(Path.Combine(full, "index.html")))
            {
                return full;
            }

            var nested = Path.Combine(full, "Web");
            return File.Exists(Path.Combine(nested, "index.html")) ? nested : null;
        }

        // 开发期路径：NET/src/Tiisu.Shell/bin/<cfg>/<tfm>/ → 上溯 7 层到仓库根，再进 Web。
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; depth < 12 && directory is not null; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Web");
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }
        }

        return null;
    }
}
