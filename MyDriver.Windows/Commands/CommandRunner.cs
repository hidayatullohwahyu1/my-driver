using System.Diagnostics;
using System.Globalization;
using System.Text;
using MyDriver.Core.Logging;

namespace MyDriver.Windows.Commands;

public sealed class CommandRunner : ICommandRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private readonly ILogService _log;
    private readonly Encoding _outputEncoding;

    static CommandRunner()
    {
        // Perlu untuk membaca output konsol berkode halaman OEM (437, 850, dst.).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public CommandRunner(ILogService log, Encoding? outputEncoding = null)
    {
        _log = log;
        _outputEncoding = outputEncoding ?? GetConsoleEncoding();
    }

    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveExecutable(fileName);
        var commandLine = FormatCommandLine(resolved, arguments);
        var limit = timeout ?? DefaultTimeout;

        var startInfo = new ProcessStartInfo(resolved)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = _outputEncoding,
            StandardErrorEncoding = _outputEncoding,
            WorkingDirectory = Environment.SystemDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _log.Info($"Menjalankan: {commandLine}");
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Error($"Perintah gagal dijalankan: {commandLine}", ex);
            return new CommandResult(commandLine, -1, string.Empty, ex.Message, stopwatch.Elapsed, TimedOut: false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = new CancellationTokenSource(limit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            if (cancellationToken.IsCancellationRequested)
            {
                _log.Warning($"Perintah dibatalkan: {commandLine}");
                throw;
            }
            timedOut = true;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        stopwatch.Stop();

        var exitCode = timedOut ? -1 : process.ExitCode;
        var result = new CommandResult(commandLine, exitCode, stdout, stderr, stopwatch.Elapsed, timedOut);
        LogResult(result, limit);
        return result;
    }

    private void LogResult(CommandResult result, TimeSpan limit)
    {
        var seconds = result.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        var header = result.TimedOut
            ? $"Waktu habis ({limit.TotalSeconds:0} dtk), proses dihentikan: {result.CommandLine}"
            : $"Selesai dengan kode keluar {result.ExitCode} dalam {seconds} dtk: {result.CommandLine}";

        var sb = new StringBuilder(header);
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            sb.AppendLine().AppendLine("--- output ---").Append(result.StandardOutput.TrimEnd());
        }
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            sb.AppendLine().AppendLine("--- error ---").Append(result.StandardError.TrimEnd());
        }

        _log.Write(result.Succeeded ? LogLevel.Info : LogLevel.Warning, sb.ToString());
    }

    /// <summary>
    /// Nama perintah tanpa path (misalnya "pnputil") dicari di System32 lebih dulu,
    /// agar exe palsu di folder flashdisk tidak ikut terjalankan.
    /// </summary>
    internal static string ResolveExecutable(string fileName)
    {
        if (Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        var name = Path.HasExtension(fileName) ? fileName : fileName + ".exe";
        var inSystem32 = Path.Combine(Environment.SystemDirectory, name);
        return File.Exists(inSystem32) ? inSystem32 : fileName;
    }

    internal static string FormatCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        static string Quote(string value) =>
            value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Contains('"')
                ? "\"" + value.Replace("\"", "\\\"") + "\""
                : value;

        return string.Join(' ', new[] { Quote(fileName) }.Concat(arguments.Select(Quote)));
    }

    private static Encoding GetConsoleEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
