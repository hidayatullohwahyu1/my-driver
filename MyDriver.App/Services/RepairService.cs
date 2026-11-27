using MyDriver.Core.Backups;
using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Logging;
using MyDriver.Core.Repair;
using MyDriver.Windows.Commands;
using MyDriver.Windows.Devices;
using MyDriver.Windows.Drivers;

namespace MyDriver.App.Services;

/// <summary>Hasil satu aksi dari kartu masalah.</summary>
public sealed record RepairOutcome(bool Ran, bool Success, bool RebootRequired = false)
{
    public static RepairOutcome NotRun { get; } = new(false, false);
}

/// <summary>
/// Menjalankan rekomendasi dari kartu masalah dengan semua pengaman F5/F6:
/// hak Administrator, daftar terlindung, konfirmasi yang menyebut akibatnya, restore point
/// sebelum mengubah driver, satu aksi dalam satu waktu, catatan Riwayat, dan scan ulang setelahnya.
/// </summary>
public sealed class RepairService
{
    /// <summary>Aksi bertombol yang belum dibuat (Windows Update menyusul di Tahap 6).</summary>
    public static readonly IReadOnlySet<string> NotYetAvailable = new HashSet<string> { ActionCatalog.WindowsUpdate };

    private readonly WorkspaceService _workspace;
    private readonly ScanCoordinator _scan;
    private readonly DriverActions _driverActions;
    private readonly DeviceControlService _devices;
    private readonly DriverInstaller _installer;
    private readonly SystemTools _system;
    private readonly ILogService _log;

    public RepairService(
        WorkspaceService workspace, ScanCoordinator scan, DriverActions driverActions,
        DeviceControlService devices, DriverInstaller installer, SystemTools system, ILogService log)
    {
        _workspace = workspace;
        _scan = scan;
        _driverActions = driverActions;
        _devices = devices;
        _installer = installer;
        _system = system;
        _log = log;
    }

    public bool CanModify => _driverActions.CanModify;

    public async Task<RepairOutcome> ExecuteAsync(Finding finding, string actionId)
    {
        var info = ActionCatalog.Get(actionId);
        var device = finding.Device;

        // Aksi yang tidak mengubah driver perangkat.
        switch (actionId)
        {
            case ActionCatalog.BackupNow:
                var ok = await _driverActions.BackupNowAsync();
                return new RepairOutcome(true, ok);
            case ActionCatalog.SystemRestore:
                return Record(finding, info, _system.OpenSystemRestore(), restorePoint: null, rescan: false);
            case ActionCatalog.RestartWindows:
                if (!Dialogs.Confirm("Restart Windows sekarang?\n\nSimpan dan tutup pekerjaan Anda lebih dulu. Windows akan restart dalam 15 detik."))
                {
                    return RepairOutcome.NotRun;
                }
                return Record(finding, info, await _system.RestartWindowsAsync(), restorePoint: null, rescan: false);
        }

        if (info.RequiresAdministrator && !_driverActions.CheckAdministrator(info.Label))
        {
            return RepairOutcome.NotRun;
        }
        if (!_driverActions.CheckReady())
        {
            return RepairOutcome.NotRun;
        }

        var check = ActionPolicy.Check(actionId, device);
        if (check.Permission == ActionPermission.Blocked)
        {
            Dialogs.Warning(check.Reason!);
            return RepairOutcome.NotRun;
        }

        // Siapkan file driver lebih dulu agar pengguna tahu sebelum restore point dibuat.
        string? infPath = null;
        string? sourceText = null;
        if (actionId is ActionCatalog.RollbackToSnapshot or ActionCatalog.InstallFromBackup)
        {
            (infPath, sourceText) = actionId == ActionCatalog.RollbackToSnapshot ? FindSnapshotDriver(device!) : FindBackupDriver(device!);
            if (infPath is null)
            {
                Dialogs.Info(sourceText!);
                return RepairOutcome.NotRun;
            }
        }

        if (!Confirm(info, device, check, sourceText))
        {
            return RepairOutcome.NotRun;
        }

        var outcome = RepairOutcome.NotRun;
        var started = await _workspace.RunExclusiveAsync($"{info.Label}: {device?.Name ?? "sistem"}…", async () =>
        {
            string? restorePoint = null;
            if (ActionPolicy.NeedsRestorePoint(actionId))
            {
                restorePoint = await _driverActions.EnsureRestorePointAsync($"My Driver!: {info.Label} - {device?.Name}");
                if (restorePoint is null)
                {
                    return;
                }
            }

            var result = await RunAsync(actionId, device!, infPath);
            outcome = Record(finding, info, result, restorePoint, rescan: true);
        });
        DriverActions.NotifyIfBusy(started);

        if (outcome.Ran)
        {
            await _scan.ScanAsync();
        }
        return outcome;
    }

    private Task<DriverOperationResult> RunAsync(string actionId, DeviceInfo device, string? infPath) => actionId switch
    {
        ActionCatalog.RestartDevice => _devices.RestartAsync(device.InstanceId, device.Name),
        ActionCatalog.EnableDevice => _devices.EnableAsync(device.InstanceId, device.Name),
        ActionCatalog.DisableEnableDevice => _devices.DisableThenEnableAsync(device.InstanceId, device.Name),
        ActionCatalog.UninstallAndRescan => _devices.RemoveAndRescanAsync(device.InstanceId, device.Name),
        ActionCatalog.RescanHardware => _devices.RescanAsync(),
        ActionCatalog.ReenableDriverService => _system.ReenableDriverServiceAsync(device.Service, device.Name),
        ActionCatalog.RollbackToSnapshot or ActionCatalog.InstallFromBackup => _installer.ForceInstallAsync(device.HardwareIds, infPath!, device.Name),
        _ => Task.FromResult(new DriverOperationResult(false, "Aksi ini belum tersedia.")),
    };

    private static bool Confirm(ActionInfo info, DeviceInfo? device, ActionCheck check, string? sourceText)
    {
        var lines = new List<string> { $"{info.Label}: {device?.Name ?? "sistem"}?", string.Empty, info.Description };
        if (device is not null && info.Id != ActionCatalog.RescanHardware)
        {
            lines.Add(ActionPolicy.Impact(device));
        }
        if (sourceText is not null)
        {
            lines.Add(sourceText);
        }
        if (ActionPolicy.NeedsRestorePoint(info.Id))
        {
            lines.Add("Restore point dibuat lebih dulu.");
        }
        if (check.Permission == ActionPermission.AllowedWithRedWarning)
        {
            lines.Add(string.Empty);
            lines.Add(check.Reason!);
        }

        var dangerous = check.Permission == ActionPermission.AllowedWithRedWarning || info.Id == ActionCatalog.UninstallAndRescan;
        if (!Dialogs.Confirm(string.Join("\n", lines), dangerous))
        {
            return false;
        }
        // Perangkat terlindung: konfirmasi ganda.
        return check.Permission != ActionPermission.AllowedWithRedWarning
            || Dialogs.Confirm($"Yakin mengganti driver {device!.Name}? Ini perangkat sistem penting.", dangerous: true);
    }

    /// <summary>Mencari file INF versi snapshot: di DriverStore dulu, lalu di backup.</summary>
    private (string? Inf, string Text) FindSnapshotDriver(DeviceInfo device)
    {
        if (_workspace.Snapshot is not { } snapshot)
        {
            return (null, "Belum ada snapshot sehat untuk komputer ini, jadi tidak ada versi untuk dikembalikan. Coba rekomendasi berikutnya.");
        }
        if (DiagnosisEngine.FindInSnapshot(snapshot, device) is not { DriverVersion: { } version } healthy)
        {
            return (null, $"{device.Name} tidak tercatat di snapshot sehat. Coba rekomendasi berikutnya.");
        }

        if (healthy.InfName?.StartsWith("oem", StringComparison.OrdinalIgnoreCase) == true
            && DriverStoreLocator.GetInfPath(healthy.InfName) is { } storeInf
            && string.Equals(InfReader.ReadDriverVersion(storeInf), version, StringComparison.OrdinalIgnoreCase))
        {
            return (storeInf, $"Versi yang dipasang: {version} (dari DriverStore Windows).");
        }

        foreach (var backup in _workspace.Backups.Where(b => b.IsComplete))
        {
            if (BackupManifestBuilder.FindPackageFor(backup.Manifest!, device) is { InfFile: { } inf } package
                && string.Equals(package.Version, version, StringComparison.OrdinalIgnoreCase))
            {
                return (Path.Combine(backup.Directory, inf), $"Versi yang dipasang: {version} (dari backup {Format.Date(backup.CreatedAt)}).");
            }
        }

        return (null, $"File driver {device.Name} versi {version} tidak ditemukan lagi di DriverStore maupun di backup. Coba rekomendasi berikutnya.");
    }

    private (string? Inf, string Text) FindBackupDriver(DeviceInfo device)
    {
        var complete = _workspace.Backups.Where(b => b.IsComplete).ToList();
        foreach (var backup in complete)
        {
            if (BackupManifestBuilder.FindPackageFor(backup.Manifest!, device) is { InfFile: { } inf } package)
            {
                return (Path.Combine(backup.Directory, inf),
                    $"Versi di backup {Format.Date(backup.CreatedAt)}: {package.Version ?? "tidak diketahui"}.");
            }
        }
        return (null, complete.Count == 0
            ? "Belum ada backup driver untuk komputer ini. Buat backup saat semua perangkat berfungsi normal."
            : $"Belum ada backup yang berisi driver {device.Name}. Coba rekomendasi berikutnya.");
    }

    private RepairOutcome Record(Finding finding, ActionInfo info, DriverOperationResult result, string? restorePoint, bool rescan)
    {
        _log.Info($"Aksi {info.Id} untuk {finding.Device?.Name ?? finding.Title}: {(result.Success ? "berhasil" : "gagal")} - {result.Message}");
        _workspace.AddHistory(new HistoryEntry
        {
            Time = DateTime.Now,
            Kind = HistoryKind.Action,
            Title = finding.Device is { } d ? $"{info.Label}: {d.Name}" : info.Label,
            Success = result.Success,
            Message = result.Message,
            ActionId = info.Id,
            DeviceName = finding.Device?.Name,
            InstanceId = finding.Device?.InstanceId,
            RestorePoint = restorePoint,
            Commands = result.Command is { } c ? [c.CommandLine] : [],
        });

        if (!result.Success)
        {
            Dialogs.Error(result.Message + "\n\nRincian ada di halaman Riwayat dan folder log.");
        }
        else if (!rescan || result.RebootRequired)
        {
            Dialogs.Info(result.Message);
        }
        return new RepairOutcome(true, result.Success, result.RebootRequired);
    }
}
