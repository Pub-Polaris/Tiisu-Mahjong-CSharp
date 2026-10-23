namespace Tiisu.DevTools;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DevToolsForm(Toolchain.Discover()));
    }
}
