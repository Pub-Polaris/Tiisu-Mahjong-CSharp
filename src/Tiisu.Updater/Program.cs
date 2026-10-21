using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Tiisu.Updater;

/// <summary>
/// 替换式更新：把新构建覆盖到已安装目录，不卸载、不动 %APPDATA% 里的存档。
///
/// 用法（由安装器/程序或人手调用）：
///   Tiisu.Updater.exe --apply "D:\downloads\Tiisu-v2.zip"
///   Tiisu.Updater.exe --apply "E:\build\publish" --restart
///
/// 行为：
///   1. 等目标进程退出（默认 Tiisu.Mahjong，最多 60 秒）
///   2. 当前安装目录整体备份到 安装目录\backup\update_时间戳（备份失败就不继续）
///   3. 源（目录或 zip）覆盖到安装目录；保留 backup 与 version.json 之外的旧文件不动
///   4. 写 version.json（记录更新时间与来源）
///   5. --restart 时重新拉起主程序
/// </summary>
internal static class Program
{
    private const string AppExe = "Tiisu.Mahjong.exe";
    private const string ProcessName = "Tiisu.Mahjong";

    private static int Main(string[] args)
    {
        string? source = null;
        string? installDir = null;
        bool restart = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--apply":
                    source = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--install":
                    installDir = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--restart":
                    restart = true;
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine("Tiisu.Updater --apply <目录或 zip> [--install <安装目录>] [--restart]");
                    return 0;
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine("缺少 --apply <目录或 zip>");
            return 2;
        }

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            Console.Error.WriteLine("源不存在: " + source);
            return 2;
        }

        installDir ??= AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(installDir))
        {
            Console.Error.WriteLine("安装目录不存在: " + installDir);
            return 2;
        }

        try
        {
            WaitForAppExit();
            string payload = ResolvePayload(source);
            string backup = Backup(installDir);
            Log($"备份: {backup}");
            CopyTree(payload, installDir);
            WriteVersion(installDir, source, payload);
            Log("更新完成: " + installDir);

            if (restart)
            {
                string exe = Path.Combine(installDir, AppExe);
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = installDir, UseShellExecute = true });
                    Log("已重新启动: " + exe);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log("更新失败: " + ex.Message);
            return 1;
        }
        finally
        {
            if (payloadTemp is not null && Directory.Exists(payloadTemp))
            {
                try
                {
                    Directory.Delete(payloadTemp, recursive: true);
                }
                catch
                {
                    // 忽略清理失败
                }
            }
        }
    }

    private static string? payloadTemp;

    private static string ResolvePayload(string source)
    {
        if (Directory.Exists(source))
        {
            return Path.GetFullPath(source);
        }

        payloadTemp = Path.Combine(Path.GetTempPath(), "tiisu-update-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(source, payloadTemp);

        // zip 里如果套了一层目录，就往下找含主程序的目录
        if (File.Exists(Path.Combine(payloadTemp, AppExe)))
        {
            return payloadTemp;
        }

        foreach (var dir in Directory.EnumerateDirectories(payloadTemp, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(dir, AppExe)))
            {
                return dir;
            }
        }

        return payloadTemp;
    }

    private static void WaitForAppExit()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var running = Process.GetProcessesByName(ProcessName);
            if (running.Length == 0)
            {
                return;
            }

            foreach (var p in running)
            {
                p.Dispose();
            }

            Thread.Sleep(400);
        }

        throw new TimeoutException("主程序仍在运行，已等待 60 秒");
    }

    private static string Backup(string installDir)
    {
        string dir = Path.Combine(installDir, "backup", "update_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(dir);
        CopyTree(installDir, dir, skipBackup: true);
        return dir;
    }

    private static void CopyTree(string from, string to, bool skipBackup = false)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(from, file);
            if (skipBackup && rel.StartsWith("backup" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string dest = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            // Updater 自己正在运行，覆盖会锁失败：跳过自身文件（它只随安装包更新）
            if (Path.GetFileName(rel).StartsWith("Tiisu.Updater", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyWithRetry(file, dest);
        }
    }

    /// <summary>刚退出的进程可能还占着句柄：最多重试 30 秒。</summary>
    private static void CopyWithRetry(string from, string to)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void WriteVersion(string installDir, string source, string payloadDir)
    {
        // 优先保留新包自带 version.json 的版本号/构建时间，只补 updatedAt/source
        var fields = new Dictionary<string, object>();
        string payloadVersion = Path.Combine(payloadDir, "version.json");
        if (File.Exists(payloadVersion))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(payloadVersion));
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    fields[p.Name] = p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString()!
                        : (object)p.Value.GetRawText();
                }
            }
            catch
            {
                // 坏文件就忽略，下面照常写新的
            }
        }

        fields["updatedAt"] = DateTime.Now.ToString("s");
        fields["source"] = source;
        fields["app"] = AppExe;

        File.WriteAllText(Path.Combine(installDir, "version.json"),
            JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private static void Log(string message)
    {
        Console.WriteLine("[updater] " + message);
        try
        {
            string log = Path.Combine(AppContext.BaseDirectory, "updater.log");
            File.AppendAllText(log, DateTime.Now.ToString("s") + " " + message + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
            // 日志失败不影响更新
        }
    }
}
