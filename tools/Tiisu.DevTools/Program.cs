namespace Tiisu.DevTools;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 带参数时直接转发给开发命令（审计 P3-5：开发入口从主程序挪到 DevTools）。
        if (args.Length > 0)
        {
            Tiisu.App.Program.EnsureConsole();
            Tiisu.App.Program.TryUseUtf8Console();
            Environment.ExitCode = DevCommands.Run(args) ?? 2;
            return;
        }

        // 显式初始化，避免与 App 程序集里同名的 ApplicationConfiguration 生成类型冲突（CS0436）。
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new DevToolsForm(Toolchain.Discover()));
    }
}
