using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Tiisu.Updater;

/// <summary>
/// 替换式更新的文件操作核心：与进程等待/重启解耦，便于单测。
/// </summary>
public static class UpdateCore
{
    public const string AppExe = "Tiisu.Mahjong.exe";

    /// <summary>解析目录或 zip 源；zip 会解到临时目录。tempDir 由调用方负责清理。</summary>
    public static string ResolvePayload(string source, out string? tempDir)
    {
        tempDir = null;
        if (Directory.Exists(source))
        {
            return Path.GetFullPath(source);
        }

        tempDir = Path.Combine(Path.GetTempPath(), "tiisu-update-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(source, tempDir);

        // zip 里如果套了一层目录，就往下找含主程序的目录
        if (File.Exists(Path.Combine(tempDir, AppExe)))
        {
            return tempDir;
        }

        foreach (var dir in Directory.EnumerateDirectories(tempDir, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(dir, AppExe)))
            {
                return dir;
            }
        }

        return tempDir;
    }

    /// <summary>备份当前安装目录，并按份数修剪旧备份；返回本次备份目录。</summary>
    public static string Backup(string installDir, int keep = 3, DateTime? now = null)
    {
        string dir = Path.Combine(installDir, "backup", "update_" + (now ?? DateTime.Now).ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(dir);
        CopyTree(installDir, dir, skipBackup: true);
        PruneBackups(Path.Combine(installDir, "backup"), keep);
        return dir;
    }

    /// <summary>只保留最近 N 份备份，避免更新器把磁盘吃满。</summary>
    public static void PruneBackups(string backupRoot, int keep)
    {
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        var dirs = Directory.GetDirectories(backupRoot, "update_*")
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .Skip(keep)
            .ToArray();
        foreach (string old in dirs)
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch
            {
                // 删不掉就留着
            }
        }
    }

    /// <summary>把 from 覆盖到 to；skipUpdaterFiles 时跳过更新器自身（正在运行会被锁）。</summary>
    public static void CopyTree(string from, string to, bool skipBackup = false, bool skipUpdaterFiles = false)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(from, file);
            if (skipBackup && rel.StartsWith("backup" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (skipUpdaterFiles && Path.GetFileName(rel).StartsWith("Tiisu.Updater", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string dest = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
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

    /// <summary>写 version.json：保留新包自带字段与 JSON 类型，补 updatedAt/source/app。</summary>
    public static void WriteVersion(string installDir, string source, string payloadDir)
    {
        var fields = new Dictionary<string, object>();
        string payloadVersion = Path.Combine(payloadDir, "version.json");
        if (File.Exists(payloadVersion))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(payloadVersion));
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    fields[p.Name] = p.Value.Clone();
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
}
