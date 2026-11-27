using MyDriver.Core.Backups;
using MyDriver.Core.Devices;
using MyDriver.Core.Logging;
using MyDriver.Core.Storage;
using MyDriver.Windows.Commands;

namespace MyDriver.Windows.Drivers;

public sealed record DriverOperationResult(bool Success, string Message, CommandResult? Command = null, bool RebootRequired = false);

/// <summary>Backup (DISM) dan restore (pnputil) driver untuk satu komputer.</summary>
public sealed class DriverBackupService
{
    private readonly DismService _dism;
    private readonly PnpUtilService _pnputil;
    private readonly ILogService _log;
    private readonly Func<string, string?> _driverStoreFolderOf;

    public DriverBackupService(DismService dism, PnpUtilService pnputil, ILogService log, Func<string, string?>? driverStoreFolderOf = null)
    {
        _dism = dism;
        _pnputil = pnputil;
        _log = log;
        _driverStoreFolderOf = driverStoreFolderOf ?? DriverStoreLocator.GetPackageFolderName;
    }

    /// <summary>Ekspor semua driver pihak ketiga ke <c>backups/yyyy-MM-dd</c>, lalu tulis manifest.</summary>
    public async Task<(DriverOperationResult Result, BackupInfo? Backup)> BackupAsync(ComputerStore store, ScanResult scan)
    {
        var now = DateTime.Now;
        var directory = store.CreateBackupDirectory(now);
        _log.Info($"Backup driver dimulai ke {directory}");

        var command = await _dism.ExportDriversAsync(directory).ConfigureAwait(false);
        var exported = Directory.EnumerateDirectories(directory)
            .Select(d => new ExportedPackage(
                Path.GetFileName(d),
                Directory.EnumerateFiles(d, "*.inf").Select(f => Path.GetRelativePath(directory, f)).FirstOrDefault()))
            .ToList();

        if (!command.Succeeded)
        {
            if (exported.Count == 0)
            {
                TryDelete(directory);
            }
            var reason = command.TimedOut ? "waktu habis" : $"kode keluar {command.ExitCode}";
            return (new DriverOperationResult(false, $"Backup gagal ({reason}). {LastLine(command)}", command), null);
        }

        var packages = BackupManifestBuilder.BuildPackages(exported, scan, _driverStoreFolderOf);
        var size = ComputerStore.DirectorySize(directory);
        var manifest = new BackupManifest
        {
            CreatedAt = now,
            ComputerId = store.Profile.Id,
            ComputerFolder = store.Profile.FolderName,
            Windows = scan.Computer.WindowsText,
            TotalBytes = size,
            Packages = packages,
        };
        store.SaveBackupManifest(directory, manifest);

        _log.Info($"Backup selesai: {packages.Count} paket driver, {size / 1024 / 1024} MB.");
        var info = new BackupInfo(directory, Path.GetFileName(directory), now, manifest, size);
        return (new DriverOperationResult(true, $"Backup selesai: {packages.Count} paket driver tersimpan.", command), info);
    }

    /// <summary>Memasang semua paket driver dari sebuah backup.</summary>
    public async Task<DriverOperationResult> RestoreAllAsync(BackupInfo backup)
    {
        _log.Info($"Restore semua driver dari {backup.Directory}");
        var command = await _pnputil.AddDriverAsync(Path.Combine(backup.Directory, "*.inf"), includeSubdirectories: true).ConfigureAwait(false);
        return Describe(command, "Semua driver dari backup");
    }

    /// <summary>Memasang satu paket driver dari backup.</summary>
    public async Task<DriverOperationResult> RestorePackageAsync(BackupInfo backup, BackupPackage package)
    {
        if (package.InfFile is null)
        {
            return new DriverOperationResult(false, "File INF paket ini tidak ditemukan di folder backup.");
        }

        var inf = Path.Combine(backup.Directory, package.InfFile);
        if (!File.Exists(inf))
        {
            return new DriverOperationResult(false, $"File tidak ditemukan: {inf}");
        }

        _log.Info($"Restore paket {package.FolderName} dari {backup.Directory}");
        var command = await _pnputil.AddDriverAsync(inf, includeSubdirectories: false).ConfigureAwait(false);
        return Describe(command, $"Driver {package.DisplayName}");
    }

    private static DriverOperationResult Describe(CommandResult command, string subject) =>
        PnpUtilService.Interpret(command) switch
        {
            PnpUtilOutcome.Success => new(true, $"{subject} berhasil dipasang.", command),
            PnpUtilOutcome.SuccessRebootRequired => new(true, $"{subject} berhasil dipasang. Restart Windows untuk menyelesaikan.", command, RebootRequired: true),
            PnpUtilOutcome.NoChange => new(true, $"{subject} sudah ada di sistem; tidak ada driver perangkat yang berubah.", command),
            _ => new(false, $"{subject} gagal dipasang (kode keluar {command.ExitCode}). {LastLine(command)}", command),
        };

    private static string LastLine(CommandResult command) =>
        (command.StandardError + "\n" + command.StandardOutput)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;

    private void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            _log.Warning($"Folder backup gagal yang kosong tidak bisa dihapus: {directory}", ex);
        }
    }
}
