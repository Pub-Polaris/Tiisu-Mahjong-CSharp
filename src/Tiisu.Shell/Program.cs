using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Web.WebView2.WinForms;

namespace Tiisu.Shell;

internal static class Program
{
    private const string Usage = """
        Tiisu Mahjong · C# Shell（WinForms + WebView2）

        用法:
          Tiisu.Mahjong                         打开门户页（WebView2 宿主）
          Tiisu.Mahjong --open index.html      直接打开牌桌
          Tiisu.Mahjong --auto 4               自动开局（东风场）并进入牌桌
          Tiisu.Mahjong --web <dir>             指定 Web 目录（含 index.html）
          Tiisu.Mahjong --smoke <seconds>       无头自检：auto=1&mode=1，到时输出日志并退出
          Tiisu.Mahjong --smoke 25 --auto 4     无头自检：跑完 4 局目标
          Tiisu.Mahjong --shot <png>            加载 3 秒后截图，写 PNG 后退出
          Tiisu.Mahjong --open index.html --shot table.png  牌桌页截图
          Tiisu.Mahjong --smoke 15 --shot out.png   两个开关可同时使用
          Tiisu.Mahjong --no-auto --delay 6000  调试：人类对局模式 / 改截图等待毫秒
          Tiisu.Mahjong --smoke 20 --drive-human  调试：人类模式自动出牌（验证人类桥）

        数据目录: %APPDATA%\TiisuMahjong（settings / state / stats / logs）
        退出码: 0 = 通过；1 = JS 异常 / 进程失败 / 未跑完至少一局；2 = 环境问题。
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        var options = ShellOptions.Parse(args);
        bool headless = options.ShowHelp || options.SmokeSeconds > 0 || options.ShotPath is not null;
        if (headless)
        {
            AttachConsole(AttachParentProcess);
            TryUseUtf8();
        }
        else
        {
            // 控制台子系统只为让 --smoke 的 stdout 能被 dotnet run 捕获；
            // 正常开窗时脱离控制台，避免多出一个黑框。
            FreeConsole();
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var webDirectory = WebDirectoryLocator.Resolve(options.WebDirectory);
        if (webDirectory is null)
        {
            Console.Error.WriteLine(
                "[shell] 找不到 Web/index.html。请用 --web <目录> 指定，或把 Shell 放在仓库的 NET/src 下构建。");
            return 2;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var form = new MainForm(options, webDirectory);
        Application.Run(form);
        return form.ExitCode;
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private static void TryUseUtf8()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch
        {
            // 控制台被重定向 / 无控制台时忽略。
        }
    }
}
