using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Tiisu.Localization;

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
    private const string AppExe = UpdateCore.AppExe;
    private const string ProcessName = "Tiisu.Mahjong";

    private static int Main(string[] args)
    {
        // CLI 文案跟随 TIISU_LANG（与 App 同一约定）；缺省 zh-CN，保持旧输出不变。
        L.SetLanguage(Environment.GetEnvironmentVariable("TIISU_LANG"));
        string? source = null;
        string? installDir = null;
          string? expectedSha256 = null;
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
                case "--sha256":
                    expectedSha256 = i + 1 < args.Length ? args[++i] : null;
                    break;

                case "--restart":
                    restart = true;
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(L.T("updater.cli.usage"));
                    return 0;
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine(L.T("updater.err.applyRequired"));
            return 2;
        }

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            Console.Error.WriteLine(L.T("updater.err.sourceNotFound", ("path", source)));
            return 2;
        }

        installDir ??= AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(installDir))
        {
            Console.Error.WriteLine(L.T("updater.err.installDirNotFound", ("path", installDir)));
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            if (!File.Exists(source))
            {
                Console.Error.WriteLine(L.T("updater.err.sha256FileOnly"));
                return 2;
            }

            string actual;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(source))
            {
                actual = Convert.ToHexString(sha.ComputeHash(stream));
            }

            if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(L.T("updater.err.sha256Mismatch",
                    ("expected", expectedSha256), ("actual", actual)));
                return 3;
            }
        }

        string? backup = null;
        string? payloadTemp = null;
        try
        {
            WaitForAppExit();
            string payload = UpdateCore.ResolvePayload(source, out payloadTemp);
            backup = UpdateCore.Backup(installDir);
            Log(L.T("updater.log.backup", ("path", backup)));
            UpdateCore.CopyTree(payload, installDir, skipUpdaterFiles: true);
            UpdateCore.WriteVersion(installDir, source, payload);
            Log(L.T("updater.log.updated", ("path", installDir)));

            if (restart)
            {
                string exe = Path.Combine(installDir, AppExe);
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = installDir, UseShellExecute = true });
                    Log(L.T("updater.log.restarted", ("path", exe)));
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log(L.T("updater.err.updateFailed", ("message", ex.Message)));

            // 半覆盖比失败更糟：先用备份还原（P2-9）。
            if (backup is not null && Directory.Exists(backup))
            {
                try
                {
                    Log(L.T("updater.log.rollbackStart", ("path", backup)));
                    UpdateCore.CopyTree(backup, installDir);
                    Log(L.T("updater.log.rollbackDone"));
                }
                catch (Exception rollbackError)
                {
                    Log(L.T("updater.err.rollbackFailed",
                        ("backup", backup), ("message", rollbackError.Message)));
                }
            }

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

        throw new TimeoutException(L.T("updater.err.appStillRunning"));
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
