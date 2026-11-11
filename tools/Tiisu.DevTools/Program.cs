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

        ApplicationConfiguration.Initialize();
        Application.Run(new DevToolsForm(Toolchain.Discover()));
    }
}
