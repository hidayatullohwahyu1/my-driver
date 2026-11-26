using MyDriver.Core.Logging;

namespace MyDriver.Tests;

public class FileLogServiceTests
{
    [Fact]
    public void Write_CreatesDailyFileWithLevelAndMessage()
    {
        using var temp = new TempDirectory();
        var now = new DateTime(2026, 11, 26, 15, 0, 1);
        ILogService log = new FileLogService(temp.Path, () => now);

        log.Info("Scan dimulai");
        log.Error("Gagal", new InvalidOperationException("contoh galat"));

        var file = Path.Combine(temp.Path, "2026-11-26.log");
        Assert.True(File.Exists(file));
        var text = File.ReadAllText(file);
        Assert.Contains("2026-11-26 15:00:01.000 [INFO] Scan dimulai", text);
        Assert.Contains("[GALAT] Gagal", text);
        Assert.Contains("contoh galat", text);
    }

    [Fact]
    public void Write_FromManyThreads_KeepsEveryLine()
    {
        using var temp = new TempDirectory();
        var log = new FileLogService(temp.Path);

        Parallel.For(0, 200, i => log.Write(LogLevel.Info, $"baris {i}"));

        var lines = File.ReadAllLines(log.CurrentLogFile);
        Assert.Equal(200, lines.Length);
    }
}
