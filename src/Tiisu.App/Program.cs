using System;
using System.IO;
using System.Linq;
using Tiisu.App.Assets;
using Tiisu.App.Ui;

namespace Tiisu.App;

/// <summary>
/// 出货主程序入口：只负责 GUI 与 <c>--version</c>。
/// 开发/自检命令在 <c>tools/Tiisu.DevTools</c>（审计 P3-5）。
/// </summary>
internal static class Program
{
    internal static readonly string AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            EnsureConsole();
            TryUseUtf8Console();

            if (args[0] == "--version")
            {
                Console.WriteLine(BuildInfo.Version);
                return;
            }

            if (args[0] != "--cpu-render")
            {
                Console.WriteLine($"未知参数：{args[0]}");
                Console.WriteLine("开发/自检命令请用 tools/Tiisu.DevTools（同一批参数）。");
                return;
            }
        }

        ApplicationConfiguration.Initialize();
        bool forceCpuRender = args.Contains("--cpu-render", StringComparer.Ordinal);
        var atlas = new TileAtlas(AssetsDir);   // 所有权交给 MainForm
        Application.Run(new MainForm(atlas, forceCpuRender));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    /// <summary>
    /// WinExe 从命令行启动时不继承控制台（P1-5）：优先挂到父进程控制台，
    /// 失败再新建一个；重定向输出时不动，避免破坏管道捕获。
    /// </summary>
    internal static void EnsureConsole()
    {
        try
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            if (!AttachConsole(-1) && !AllocConsole())
            {
                return;
            }

            var utf8 = new System.Text.UTF8Encoding(false);
            var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
        catch
        {
            // 控制台不可用不影响 GUI 启动
        }
    }

    /// <summary>控制台按 UTF-8 输出（重定向到管道时 .NET 默认就是 UTF-8，这里显式统一）。</summary>
    internal static void TryUseUtf8Console()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // 无控制台（纯重定向 / GUI 宿主）时忽略。
        }
    }
}
