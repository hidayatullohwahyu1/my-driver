using MyDriver.Core.Backups;
using MyDriver.Core.Storage;
using MyDriver.Windows.Commands;
using MyDriver.Windows.Drivers;

namespace MyDriver.Tests;

/// <summary>Runner palsu: mencatat perintah dan menjalankan aksi pengganti tanpa menyentuh sistem.</summary>
internal sealed class FakeCommandRunner : ICommandRunner
{
    public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];

    public Func<string, IReadOnlyList<string>, CommandResult> Handler { get; set; } =
        (file, args) => new CommandResult(file, 0, string.Empty, string.Empty, TimeSpan.Zero, false);

    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(Handler(fileName, arguments));
    }
}

public class DriverBackupServiceTests
{
    private static CommandResult Result(int exit, string output = "") =>
        new("cmd", exit, output, string.Empty, TimeSpan.Zero, false);

    [Fact]
    public void Arguments_AreBuiltCorrectly()
    {
        Assert.Equal(["/English", "/Online", "/Export-Driver", @"/Destination:D:\My Data\backups\2026-11-27"],
            DismService.ExportArguments(@"D:\My Data\backups\2026-11-27"));
        Assert.Equal(["/add-driver", @"D:\b\*.inf", "/subdirs", "/install"],
            PnpUtilService.AddDriverArguments(@"D:\b\*.inf", includeSubdirectories: true));
        Assert.Equal(["/add-driver", @"D:\b\x\x.inf", "/install"],
            PnpUtilService.AddDriverArguments(@"D:\b\x\x.inf", includeSubdirectories: false));
    }

    [Theory]
    [InlineData(0, PnpUtilOutcome.Success)]
    [InlineData(3010, PnpUtilOutcome.SuccessRebootRequired)]
    [InlineData(259, PnpUtilOutcome.NoChange)]
    [InlineData(5, PnpUtilOutcome.Failed)]
    public void Interpret_MapsPnpUtilExitCodes(int exit, PnpUtilOutcome expected) =>
        Assert.Equal(expected, PnpUtilService.Interpret(Result(exit)));

    [Fact]
    public async Task BackupAsync_Success_WritesManifestLinkedToDevices()
    {
        using var temp = new TempDirectory();
        var store = ComputerStore.Open(DataPaths.Resolve(temp.Path), TestData.Computer(), DateTime.Now, new MemoryLog());
        var runner = new FakeCommandRunner
        {
            Handler = (file, args) =>
            {
                // Tiru DISM: buat satu subfolder paket berisi file INF.
                var destination = args.Single(a => a.StartsWith("/Destination:"))["/Destination:".Length..];
                var package = Path.Combine(destination, "netathr.inf_amd64_aaa");
                Directory.CreateDirectory(package);
                File.WriteAllText(Path.Combine(package, "netathr.inf"), "[Version]");
                return Result(0);
            },
        };
        var service = new DriverBackupService(new DismService(runner), new PnpUtilService(runner), new MemoryLog(),
            inf => inf == "oem1.inf" ? "netathr.inf_amd64_aaa" : null);

        var (result, backup) = await service.BackupAsync(store, TestData.Scan(TestData.Device("WIFI", "Qualcomm WiFi")));

        Assert.True(result.Success);
        Assert.NotNull(backup);
        Assert.Equal("dism", runner.Calls.Single().File);
        var listed = Assert.Single(store.ListBackups());
        Assert.True(listed.IsComplete);
        Assert.Equal(store.Profile.Id, listed.Manifest!.ComputerId);
        var package = Assert.Single(listed.Manifest.Packages);
        Assert.Equal("Qualcomm WiFi", package.DisplayName);
        Assert.True(listed.SizeBytes > 0);
    }

    [Fact]
    public async Task BackupAsync_Failure_RemovesEmptyFolder()
    {
        using var temp = new TempDirectory();
        var store = ComputerStore.Open(DataPaths.Resolve(temp.Path), TestData.Computer(), DateTime.Now, new MemoryLog());
        var runner = new FakeCommandRunner { Handler = (_, _) => Result(740, "Error: 740\nElevated permissions are required.") };
        var service = new DriverBackupService(new DismService(runner), new PnpUtilService(runner), new MemoryLog(), _ => null);

        var (result, backup) = await service.BackupAsync(store, TestData.Scan());

        Assert.False(result.Success);
        Assert.Null(backup);
        Assert.Contains("740", result.Message);
        Assert.Empty(store.ListBackups());
    }

    [Fact]
    public async Task RestorePackageAsync_RunsPnpUtilOnThePackageInf()
    {
        using var temp = new TempDirectory();
        var dir = Path.Combine(temp.Path, "2026-11-27");
        Directory.CreateDirectory(Path.Combine(dir, "netathr.inf_amd64_aaa"));
        File.WriteAllText(Path.Combine(dir, "netathr.inf_amd64_aaa", "netathr.inf"), "[Version]");
        var package = new BackupPackage { FolderName = "netathr.inf_amd64_aaa", InfFile = @"netathr.inf_amd64_aaa\netathr.inf" };
        var backup = new BackupInfo(dir, "2026-11-27", DateTime.Now,
            new BackupManifest { CreatedAt = DateTime.Now, ComputerId = "X", Packages = [package] }, 1);
        var runner = new FakeCommandRunner { Handler = (_, _) => Result(3010) };
        var service = new DriverBackupService(new DismService(runner), new PnpUtilService(runner), new MemoryLog(), _ => null);

        var result = await service.RestorePackageAsync(backup, package);

        Assert.True(result.Success);
        Assert.True(result.RebootRequired);
        var call = runner.Calls.Single();
        Assert.Equal("pnputil", call.File);
        Assert.Equal(["/add-driver", Path.Combine(dir, @"netathr.inf_amd64_aaa\netathr.inf"), "/install"], call.Args);
    }

    [Fact]
    public async Task RestorePackageAsync_MissingInf_DoesNotRunPnpUtil()
    {
        using var temp = new TempDirectory();
        var package = new BackupPackage { FolderName = "x", InfFile = @"x\x.inf" };
        var backup = new BackupInfo(temp.Path, "b", DateTime.Now, null, 0);
        var runner = new FakeCommandRunner();
        var service = new DriverBackupService(new DismService(runner), new PnpUtilService(runner), new MemoryLog(), _ => null);

        var result = await service.RestorePackageAsync(backup, package);

        Assert.False(result.Success);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task RestoreAllAsync_UsesSubdirsPattern()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => Result(259) };
        var service = new DriverBackupService(new DismService(runner), new PnpUtilService(runner), new MemoryLog(), _ => null);
        var backup = new BackupInfo(@"D:\data\backups\2026-11-27", "2026-11-27", DateTime.Now, null, 0);

        var result = await service.RestoreAllAsync(backup);

        Assert.True(result.Success);
        Assert.Contains("tidak ada driver perangkat yang berubah", result.Message);
        Assert.Equal(["/add-driver", @"D:\data\backups\2026-11-27\*.inf", "/subdirs", "/install"], runner.Calls.Single().Args);
    }
}
