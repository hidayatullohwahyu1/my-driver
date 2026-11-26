using MyDriver.Core.Logging;
using MyDriver.Windows.Commands;

namespace MyDriver.Tests;

public class CommandRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesOutputAndExitCode_AndLogsIt()
    {
        var log = new MemoryLog();
        var runner = new CommandRunner(log);

        var result = await runner.RunAsync("cmd", ["/c", "echo halo dunia"]);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("halo dunia", result.StandardOutput.Trim());
        Assert.StartsWith(Environment.SystemDirectory, result.CommandLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(log.Entries, e => e.Message.StartsWith("Menjalankan:"));
        Assert.Contains(log.Entries, e => e.Message.Contains("kode keluar 0") && e.Message.Contains("halo dunia"));
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_IsReportedAndLoggedAsWarning()
    {
        var log = new MemoryLog();
        var runner = new CommandRunner(log);

        var result = await runner.RunAsync("cmd", ["/c", "echo gagal 1>&2 & exit 7"]);

        Assert.False(result.Succeeded);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("gagal", result.StandardError);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("kode keluar 7"));
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcess()
    {
        var runner = new CommandRunner(new MemoryLog());

        var result = await runner.RunAsync("ping", ["-n", "30", "127.0.0.1"], TimeSpan.FromMilliseconds(500));

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(result.Duration < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunAsync_Cancelled_Throws()
    {
        var runner = new CommandRunner(new MemoryLog());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync("ping", ["-n", "30", "127.0.0.1"], cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RunAsync_MissingProgram_ReturnsFailureInsteadOfThrowing()
    {
        var log = new MemoryLog();
        var runner = new CommandRunner(log);

        var result = await runner.RunAsync("program-yang-tidak-ada-12345", []);

        Assert.False(result.Succeeded);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void FormatCommandLine_QuotesArgumentsWithSpaces()
    {
        var line = CommandRunner.FormatCommandLine(
            @"C:\Windows\System32\dism.exe",
            ["/online", "/export-driver", @"/destination:D:\My Driver\backups"]);

        Assert.Equal(@"C:\Windows\System32\dism.exe /online /export-driver ""/destination:D:\My Driver\backups""", line);
    }

    [Fact]
    public void ResolveExecutable_PrefersSystem32()
    {
        Assert.Equal(
            Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            CommandRunner.ResolveExecutable("pnputil"),
            ignoreCase: true);
    }
}
