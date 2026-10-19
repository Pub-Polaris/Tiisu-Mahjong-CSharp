namespace Tiisu.Api;

public static class Program
{
    public static Task Main(string[] args)
        => TiisuApiApp.Build(args).RunAsync();
}
