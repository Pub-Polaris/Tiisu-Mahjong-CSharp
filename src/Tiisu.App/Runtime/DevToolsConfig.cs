using System;
using System.IO;
using System.Text.Json;

namespace Tiisu.App.Runtime;

/// <summary>
/// 开发命令开关（审计 P3-5）：出货版默认关闭自检/渲染入口，玩家或开发者可在
/// <c>%APPDATA%\TiisuMahjong\devtools.json</c> 里手动把 <c>enabled</c> 改成 true。
/// 用 JSON 而不是环境变量，方便双击编辑、也便于随存档备份。
/// </summary>
public static class DevToolsConfig
{
    public static string ConfigPath(string? directory = null) =>
        Path.Combine(directory ?? Settings.DefaultDirectory, "devtools.json");

    /// <summary>开关是否打开；文件缺失或写坏时按关闭处理。</summary>
    public static bool Enabled(string? directory = null)
    {
        try
        {
            string path = ConfigPath(directory);
            if (!File.Exists(path))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("enabled", out var enabled)
                   && enabled.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>首次运行时写一份带注释的模板（值仍为 false）。</summary>
    public static void EnsureTemplate(string? directory = null)
    {
        try
        {
            string path = ConfigPath(directory);
            if (File.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var template = new
            {
                enabled = false,
                note = "打开后可用 --smoke-* / --render-* / --play-real 等开发命令；普通玩家不需要改。",
            };
            File.WriteAllText(path, JsonSerializer.Serialize(template,
                new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
        }
        catch
        {
            // 写不了模板不影响游戏
        }
    }
}
