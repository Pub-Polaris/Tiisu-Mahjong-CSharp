using System;
using System.Collections.Generic;
using System.IO;

namespace Tiisu.App.Runtime;

/// <summary>
/// Steam Cloud 策略：只同步小型配置与统计；日志/语音/临时队列不云同步。
/// 真实 Steam Remote Storage 接入前，这个策略也用于本地备份/预检。
/// </summary>
public static class SteamCloudPolicy
{
    public static readonly IReadOnlySet<string> SyncFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "settings.json",
        "stats.json",
        "layout-custom.json",
    };

    public static bool ShouldSync(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return SyncFiles.Contains(Path.GetFileName(fileName.Trim()));
    }

    public static IReadOnlyList<string> ExistingFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (string name in SyncFiles)
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                result.Add(path);
            }
        }

        return result;
    }
}
