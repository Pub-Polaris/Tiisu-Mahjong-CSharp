namespace Tiisu.Api;

/// <summary>
/// Command-line / environment overrides for the API host.
/// </summary>
public sealed class TiisuApiOptions
{
    /// <summary>TCP port. 0 lets Kestrel pick a free port (used by tests).</summary>
    public int? Port { get; set; }

    public string? WebRoot { get; set; }

    public string? DataDirectory { get; set; }

    public string? ContentRoot { get; set; }
}

public sealed record ResolvedTiisuApiOptions(
    int Port,
    string WebRoot,
    string DataDirectory,
    string ContentRoot)
{
    public static ResolvedTiisuApiOptions Resolve(string[] args, TiisuApiOptions? overrides)
    {
        var cli = ParseArgs(args);

        var port = overrides?.Port
            ?? ReadInt(cli, "--port", "TIISU_PORT")
            ?? 7778;

        var webRoot = FirstNonEmpty(
            overrides?.WebRoot,
            ReadString(cli, "--web-root", "TIISU_WEB_ROOT"),
            FindWebRoot());

        if (webRoot is null)
        {
            throw new InvalidOperationException(
                "Cannot locate the Web directory (portal.html). " +
                "Pass --web-root <path> or set TIISU_WEB_ROOT.");
        }

        var dataDirectory = FirstNonEmpty(
            overrides?.DataDirectory,
            ReadString(cli, "--data-dir", "TIISU_DATA_DIR"),
            webRoot) ?? webRoot;

        var contentRoot = FirstNonEmpty(
            overrides?.ContentRoot,
            ReadString(cli, "--content-root", "TIISU_CONTENT_ROOT"),
            AppContext.BaseDirectory) ?? AppContext.BaseDirectory;

        return new ResolvedTiisuApiOptions(
            Port: port,
            WebRoot: Path.GetFullPath(webRoot),
            DataDirectory: Path.GetFullPath(dataDirectory),
            ContentRoot: Path.GetFullPath(contentRoot));
    }

    /// <summary>
    /// Walk up from the executable and the current directory looking for either
    /// a directory that contains portal.html (Web itself) or a sibling Web folder.
    /// This makes the default work from NET/src/Tiisu.Api/bin/Debug/net10.0.
    /// </summary>
    public static string? FindWebRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            for (var depth = 0; current is not null && depth < 12; depth++, current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "portal.html")))
                {
                    return current.FullName;
                }

                var sibling = Path.Combine(current.FullName, "Web");
                if (File.Exists(Path.Combine(sibling, "portal.html")))
                {
                    return sibling;
                }
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var eq = arg.IndexOf('=');
            if (eq > 2)
            {
                result[arg[..eq]] = arg[(eq + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[arg] = args[++i];
            }
            else
            {
                result[arg] = "";
            }
        }

        return result;
    }

    private static string? ReadString(
        Dictionary<string, string> cli,
        string argName,
        string envName)
    {
        if (cli.TryGetValue(argName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var env = Environment.GetEnvironmentVariable(envName);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    private static int? ReadInt(
        Dictionary<string, string> cli,
        string argName,
        string envName)
    {
        var raw = ReadString(cli, argName, envName);
        return int.TryParse(raw, out var value) ? value : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
