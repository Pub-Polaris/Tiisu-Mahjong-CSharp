namespace Tiisu.Shell;

using Tiisu.Api;

/// <summary>
/// 应用数据根目录：<c>%APPDATA%\TiisuMahjong</c>（settings / state / stats / logs 都在这里）。
/// 首次启动建目录并补齐默认 settings.json / stats.json；
/// 若目标目录还没有任何数据文件、而 Web 目录里留着旧版文件，则一次性拷贝迁移（旧文件保留）。
/// </summary>
internal static class ShellDataDirectory
{
    public const string FolderName = "TiisuMahjong";

    /// <summary>与 <see cref="DataStore"/> 的落盘名一致；迁移只看这三个文件。</summary>
    private static readonly string[] DataFiles = { "settings.json", "state.json", "stats.json" };

    public static string Resolve(string? webDirectory, Action<string> log)
    {
        string root = ResolveRoot(log);
        Directory.CreateDirectory(root);
        log("[shell] 数据目录: " + root);

        if (!string.IsNullOrWhiteSpace(webDirectory))
        {
            MigrateLegacy(webDirectory!, root, log);
        }

        EnsureDefaultFiles(root, log);
        return root;
    }

    /// <summary>
    /// 首次启动补齐 <c>settings.json</c> / <c>stats.json</c>：<see cref="DataStore"/> 只在 POST 时才落盘，
    /// 用户没改设置的话目录里会缺这两个文件。已有的文件不覆盖。
    /// </summary>
    private static void EnsureDefaultFiles(string root, Action<string> log)
    {
        try
        {
            var store = new DataStore(root);
            var written = new List<string>();
            if (!File.Exists(store.SettingsPath))
            {
                store.WriteJson(store.SettingsPath, DataStore.DefaultSettings());
                written.Add("settings.json");
            }

            if (!File.Exists(store.StatsPath))
            {
                store.WriteJson(store.StatsPath, DataStore.DefaultStats());
                written.Add("stats.json");
            }

            if (written.Count > 0)
            {
                log("[shell] 已写入默认数据文件: " + string.Join(", ", written));
            }
        }
        catch (Exception ex)
        {
            log("[shell] 初始化默认数据文件失败（继续）: " + ex.Message);
        }
    }

    private static string ResolveRoot(Action<string> log)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            log("[shell] %APPDATA% 不可用，退回 %LOCALAPPDATA%");
        }

        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = AppContext.BaseDirectory;
            log("[shell] %APPDATA% / %LOCALAPPDATA% 都不可用，退回程序目录");
        }

        return Path.GetFullPath(Path.Combine(appData, FolderName));
    }

    /// <summary>目标目录已有任一数据文件就不再迁移，保证只迁一次（源文件不动）。</summary>
    private static void MigrateLegacy(string source, string target, Action<string> log)
    {
        if (HasAny(target, DataFiles))
        {
            return;
        }

        var migrated = new List<string>();
        foreach (string name in DataFiles)
        {
            string from = Path.Combine(source, name);
            if (!File.Exists(from))
            {
                continue;
            }

            try
            {
                File.Copy(from, Path.Combine(target, name), overwrite: false);
                migrated.Add(name);
            }
            catch (Exception ex)
            {
                log($"[shell] 迁移 {name} 失败（继续）: {ex.Message}");
            }
        }

        string sourceLogs = Path.Combine(source, "logs");
        if (Directory.Exists(sourceLogs) && !Directory.Exists(Path.Combine(target, "logs")))
        {
            try
            {
                CopyDirectory(sourceLogs, Path.Combine(target, "logs"));
                migrated.Add("logs/");
            }
            catch (Exception ex)
            {
                log("[shell] 迁移 logs 失败（继续）: " + ex.Message);
            }
        }

        log(migrated.Count == 0
            ? "[shell] Web 目录没有旧数据，无需迁移"
            : "[shell] 已从 Web 目录迁移旧数据（源文件保留）: " + string.Join(", ", migrated));
    }

    private static bool HasAny(string directory, IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (File.Exists(Path.Combine(directory, name)))
            {
                return true;
            }
        }

        return false;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        foreach (string directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
