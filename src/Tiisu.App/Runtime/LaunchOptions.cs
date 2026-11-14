using System;
using System.Collections.Generic;
using Tiisu.App.Ui;

namespace Tiisu.App.Runtime;

/// <summary>命令行启动选项：Steam 启动项、QA 截图与排障共用。</summary>
public sealed record LaunchOptions(bool ForceCpuRender, bool Fullscreen, string? Language)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        bool cpu = false;
        bool fullscreen = false;
        string? language = null;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--cpu-render":
                    cpu = true;
                    break;
                case "--fullscreen":
                    fullscreen = true;
                    break;
                case "--lang" when i + 1 < args.Count:
                    language = UiText.Normalize(args[++i]);
                    break;
            }
        }

        return new LaunchOptions(cpu, fullscreen, language);
    }
}
