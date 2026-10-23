using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Tiisu.DevTools;

/// <summary>一条外部命令的执行结果。启动失败时 <see cref="ExitCode"/> 为 null。</summary>
internal sealed record CommandResult(
    string CommandLine,
    int? ExitCode,
    TimeSpan Elapsed,
    string Output,
    string? StartError)
{
    public bool Started => StartError is null;
}

/// <summary>
/// 起子进程：<c>UseShellExecute=false</c>、<c>CreateNoWindow=true</c>，异步等待退出，不碰 UI 线程。
/// </summary>
internal static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        string commandLine = FormatCommandLine(fileName, arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return new CommandResult(commandLine, null, stopwatch.Elapsed, string.Empty, "Process.Start 返回 null");
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string output = await stdout.ConfigureAwait(false);
            string error = await stderr.ConfigureAwait(false);
            stopwatch.Stop();

            return new CommandResult(
                commandLine,
                process.ExitCode,
                stopwatch.Elapsed,
                Merge(output, error),
                null);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException or PlatformNotSupportedException)
        {
            stopwatch.Stop();
            return new CommandResult(commandLine, null, stopwatch.Elapsed, string.Empty, DescribeStartError(ex, fileName));
        }
    }

    /// <summary>启动一个不需要等待的程序（游戏本体）。</summary>
    public static Process Launch(string fileName, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start 返回 null：" + fileName);
    }

    /// <summary>资源管理器打开目录，或定位到某个文件。</summary>
    public static bool OpenInExplorer(string path, bool selectFile)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (selectFile && File.Exists(path))
            {
                startInfo.ArgumentList.Add("/select," + path);
            }
            else
            {
                startInfo.ArgumentList.Add(path);
            }

            using Process? process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    public static string FormatCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder(Quote(fileName));
        foreach (string argument in arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    public static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalSeconds >= 1
        ? elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s"
        : elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;

    private static string Merge(string stdout, string stderr)
    {
        string output = stdout.TrimEnd();
        string error = stderr.TrimEnd();
        if (error.Length == 0)
        {
            return output;
        }

        return output.Length == 0 ? error : output + Environment.NewLine + error;
    }

    private static string DescribeStartError(Exception ex, string fileName) => ex switch
    {
        FileNotFoundException => "找不到可执行文件：" + fileName,
        Win32Exception win32 when win32.NativeErrorCode == 2 => "系统找不到该命令：" + fileName,
        Win32Exception win32 when win32.NativeErrorCode == 5 => "没有权限执行：" + fileName + "（" + win32.Message + "）",
        _ => ex.Message,
    };
}
