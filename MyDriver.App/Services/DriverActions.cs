using MyDriver.Core.Backups;
using MyDriver.Core.Logging;
using MyDriver.Core.Repair;
using MyDriver.Windows.Drivers;
using MyDriver.Windows.Recovery;

namespace MyDriver.App.Services;

/// <summary>
/// Aksi backup dan restore driver beserta pengamannya: hak Administrator, konfirmasi,
/// restore point sebelum mengubah driver, satu aksi dalam satu waktu, dan scan ulang setelahnya.
/// </summary>
public sealed class DriverActions
{
    private readonly AppEnvironment _environment;
    private readonly WorkspaceService _workspace;
    private readonly ScanCoordinator _scan;
    private readonly DriverBackupService _backups;
    private readonly RestorePointService _restorePoints;
    private readonly ILogService _log;

    public DriverActions(
        AppEnvironment environment, WorkspaceService workspace, ScanCoordinator scan,
        DriverBackupService backups, RestorePointService restorePoints, ILogService log)
    {
        _environment = environment;
        _workspace = workspace;
        _scan = scan;
        _backups = backups;
        _restorePoints = restorePoints;
        _log = log;
    }

    public bool CanModify => _environment.IsAdministrator;

    /// <summary>Mengembalikan true bila backup berhasil dibuat.</summary>
    public async Task<bool> BackupNowAsync()
    {
        if (!CheckAdministrator("Backup driver") || !CheckReady())
        {
            return false;
        }

        var success = false;
        var started = await _workspace.RunExclusiveAsync("Membuat backup driver… Ini bisa makan beberapa menit.", async () =>
        {
            var (result, _) = await _backups.BackupAsync(_workspace.Store!, _scan.LastResult!);
            _workspace.RefreshBackups();
            success = result.Success;
            _workspace.AddHistory(new HistoryEntry
            {
                Time = DateTime.Now,
                Kind = HistoryKind.Backup,
                Title = "Backup driver",
                Success = result.Success,
                Message = result.Message,
                Commands = result.Command is { } c ? [c.CommandLine] : [],
            });
            if (result.Success)
            {
                var old = _workspace.OldBackups.Count;
                Dialogs.Info(old > 0
                    ? $"{result.Message}\n\nAda {old} backup lama (lebih dari {BackupRetention.KeepCount}). Anda bisa menghapusnya di halaman Backup."
                    : result.Message);
            }
            else
            {
                Dialogs.Error(result.Message + "\n\nRincian ada di folder log.");
            }
        });
        NotifyIfBusy(started);
        return success;
    }

    public async Task RestoreAllAsync(BackupInfo backup)
    {
        if (!CheckAdministrator("Memulihkan driver") || !CheckReady() || !CheckOwnership(backup))
        {
            return;
        }
        if (!Dialogs.Confirm(
                $"Pasang ulang SEMUA driver dari backup {Format.DateTime(backup.CreatedAt)} ({backup.PackageCount} paket)?\n\n" +
                "Windows hanya mengganti driver perangkat bila versi di backup lebih cocok. Beberapa perangkat " +
                "(WiFi, suara, layar) bisa berhenti sebentar selama proses. Restore point dibuat lebih dulu.",
                dangerous: true))
        {
            return;
        }

        await RunRestoreAsync("Memasang driver dari backup…", "My Driver!: pulihkan semua driver dari backup",
            $"Pulihkan semua driver dari backup {Format.DateTime(backup.CreatedAt)}", () => _backups.RestoreAllAsync(backup));
    }

    public async Task RestorePackageAsync(BackupInfo backup, BackupPackage package)
    {
        if (!CheckAdministrator("Memasang driver") || !CheckReady() || !CheckOwnership(backup))
        {
            return;
        }

        var devices = package.Devices.Count > 0
            ? string.Join(", ", package.Devices.Select(d => d.Name).Distinct())
            : "perangkat yang cocok";
        if (!Dialogs.Confirm(
                $"Pasang driver dari backup untuk: {devices}?\n\n" +
                $"Versi di backup: {package.Version ?? "tidak diketahui"}.\n" +
                "Perangkat bisa berhenti sebentar selama driver dipasang. Restore point dibuat lebih dulu."))
        {
            return;
        }

        await RunRestoreAsync($"Memasang driver {package.DisplayName}…", $"My Driver!: pasang driver {package.DisplayName} dari backup",
            $"Pasang dari backup: {package.DisplayName}", () => _backups.RestorePackageAsync(backup, package));
    }

    private async Task RunRestoreAsync(
        string busyText, string restorePointDescription, string historyTitle, Func<Task<DriverOperationResult>> restore)
    {
        var started = await _workspace.RunExclusiveAsync(busyText, async () =>
        {
            var restorePoint = await EnsureRestorePointAsync(restorePointDescription);
            if (restorePoint is null)
            {
                return;
            }

            var result = await restore();
            _workspace.AddHistory(new HistoryEntry
            {
                Time = DateTime.Now,
                Kind = HistoryKind.Restore,
                Title = historyTitle,
                Success = result.Success,
                Message = result.Message,
                RestorePoint = restorePoint,
                Commands = result.Command is { } c ? [c.CommandLine] : [],
            });
            if (result.Success)
            {
                Dialogs.Info(result.Message);
            }
            else
            {
                Dialogs.Error(result.Message + "\n\nRincian ada di folder log.");
            }
        });
        NotifyIfBusy(started);

        if (started)
        {
            await _scan.ScanAsync();
        }
    }

    /// <summary>
    /// Restore point wajib sebelum mengubah driver. Bila gagal, pengguna harus mengonfirmasi dua kali.
    /// Mengembalikan keterangan untuk Riwayat, atau null bila pengguna membatalkan.
    /// </summary>
    public async Task<string?> EnsureRestorePointAsync(string description)
    {
        var result = await _restorePoints.CreateAsync(description);
        if (result.Status == RestorePointStatus.Created)
        {
            return $"Dibuat {Format.DateTime(result.PointTime ?? DateTime.Now)}";
        }
        if (result.Status == RestorePointStatus.RecentExists)
        {
            return Dialogs.Confirm($"{result.Message}\n\nLanjutkan?")
                ? $"Memakai restore point {Format.DateTime(result.PointTime!.Value)}"
                : null;
        }

        if (!Dialogs.Confirm(
                $"{result.Message}\n\nTanpa restore point, perubahan driver tidak bisa dibatalkan lewat System Restore. Tetap lanjutkan?",
                dangerous: true))
        {
            return null;
        }
        var confirmed = Dialogs.Confirm("Yakin? Lanjutkan TANPA restore point.", dangerous: true);
        _log.Warning($"Pengguna {(confirmed ? "melanjutkan" : "membatalkan")} aksi tanpa restore point.");
        return confirmed ? "Tanpa restore point (gagal dibuat, dikonfirmasi pengguna)" : null;
    }

    public bool CheckAdministrator(string action)
    {
        if (CanModify)
        {
            return true;
        }
        Dialogs.Info($"{action} butuh hak Administrator. Tekan \"Jalankan sebagai Administrator\" di bagian atas jendela.");
        return false;
    }

    public bool CheckReady()
    {
        if (_workspace.Store is not null && _scan.LastResult is not null)
        {
            return true;
        }
        Dialogs.Info(_workspace.StorageError ?? "Tunggu sampai scan perangkat selesai.");
        return false;
    }

    /// <summary>Backup milik komputer lain hanya dipakai setelah peringatan.</summary>
    private bool CheckOwnership(BackupInfo backup)
    {
        if (backup.Manifest is null)
        {
            Dialogs.Warning("Backup ini tidak lengkap (proses backup dulu tidak selesai), jadi tidak dipakai untuk memulihkan.");
            return false;
        }
        if (string.Equals(backup.Manifest.ComputerId, _workspace.Store!.Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return Dialogs.Confirm(
            $"Backup ini dibuat di komputer lain ({backup.Manifest.ComputerFolder}). Driver komputer lain bisa tidak cocok.\n\nTetap pakai?",
            dangerous: true);
    }

    public static void NotifyIfBusy(bool started)
    {
        if (!started)
        {
            Dialogs.Info("Masih ada proses lain yang berjalan. Tunggu sampai selesai.");
        }
    }
}
