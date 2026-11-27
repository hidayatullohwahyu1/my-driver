using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Core.Backups;
using MyDriver.Core.Devices;

namespace MyDriver.App.ViewModels;

/// <summary>Halaman Backup: snapshot sehat, backup sekarang, daftar backup, restore semua atau per perangkat.</summary>
public sealed partial class BackupViewModel : ObservableObject
{
    private readonly WorkspaceService _workspace;
    private readonly ScanCoordinator _scan;
    private readonly DriverActions _actions;

    public BackupViewModel(WorkspaceService workspace, ScanCoordinator scan, DriverActions actions)
    {
        _workspace = workspace;
        _scan = scan;
        _actions = actions;
        _workspace.PropertyChanged += OnWorkspaceChanged;
        _scan.PropertyChanged += (_, _) => NotifyState();
        RebuildBackups();
    }

    public ObservableCollection<BackupItemViewModel> Backups { get; } = [];

    public bool IsBusy => _workspace.IsBusy;

    public string? BusyText => _workspace.BusyText;

    public bool IsReadOnly => !_actions.CanModify;

    public bool HasScan => _workspace.Store is not null && _scan.LastResult is not null;

    /// <summary>Tombol yang mengubah sistem: butuh admin, hasil scan, dan tidak ada aksi lain.</summary>
    public bool CanModify => _actions.CanModify && HasScan && !IsBusy;

    public bool CanMarkHealthy => HasScan && !IsBusy && !_scan.IsScanning;

    public bool HasBackups => Backups.Count > 0;

    public int OldBackupCount => _workspace.OldBackups.Count;

    public bool HasOldBackups => OldBackupCount > 0;

    public string OldBackupText =>
        $"Ada {OldBackupCount} backup lama di luar {BackupRetention.KeepCount} yang terbaru. Hapus untuk menghemat ruang.";

    public bool HasSnapshot => _workspace.Snapshot is not null;

    public string SnapshotText => _workspace.Snapshot is { } s
        ? $"Dibuat {Format.DateTime(s.CreatedAt)} ({Format.Age(s.CreatedAt)}) · {s.Drivers.Count} driver"
        : "Belum ada. Tandai kondisi sekarang sebagai sehat bila semua perangkat berfungsi normal.";

    public string? StorageError => _workspace.StorageError;

    public string? DataFolder => _workspace.Store?.Directory;

    [RelayCommand]
    private void MarkHealthy()
    {
        if (_scan.LastResult is not { } scan)
        {
            return;
        }

        var problems = scan.Devices.Count(d => d.Status is DeviceStatus.Problem or DeviceStatus.Disabled);
        var message = problems > 0
            ? $"Ada {problems} perangkat yang sedang error atau dinonaktifkan. Perangkat itu tidak dimasukkan ke snapshot.\n\nTetap tandai kondisi sekarang sebagai sehat?"
            : "Tandai kondisi sekarang sebagai sehat?\n\nDaftar driver saat ini dipakai sebagai acuan untuk mendeteksi perubahan dan untuk \"kembalikan ke versi snapshot\".";
        if (HasSnapshot)
        {
            message += "\n\nSnapshot lama akan diganti.";
        }

        if (Dialogs.Confirm(message, dangerous: problems > 0))
        {
            _workspace.SaveSnapshot(scan);
            if (_workspace.NeedsOnboarding)
            {
                _workspace.CompleteOnboarding();
            }
        }
    }

    [RelayCommand]
    private Task BackupNowAsync() => _actions.BackupNowAsync();

    [RelayCommand]
    private Task RestoreAllAsync(BackupItemViewModel? item) =>
        item is null ? Task.CompletedTask : _actions.RestoreAllAsync(item.Info);

    [RelayCommand]
    private Task RestorePackageAsync(PackageItemViewModel? item) =>
        item is null ? Task.CompletedTask : _actions.RestorePackageAsync(item.Backup, item.Package);

    [RelayCommand]
    private void OpenFolder(BackupItemViewModel? item)
    {
        var folder = item?.Info.Directory ?? _workspace.Store?.BackupsDirectory;
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(BackupItemViewModel? item)
    {
        if (item is null || _workspace.Store is null || IsBusy)
        {
            return;
        }
        if (!Dialogs.Confirm(
                $"Hapus permanen backup {item.Title} ({Format.Bytes(item.Info.SizeBytes)})?\n\nFolder backup ini akan dihapus dan tidak bisa dikembalikan.",
                dangerous: true))
        {
            return;
        }
        await DeleteBackupsAsync([item.Info]);
    }

    [RelayCommand]
    private async Task DeleteOldBackupsAsync()
    {
        var old = _workspace.OldBackups;
        if (old.Count == 0 || _workspace.Store is null || IsBusy)
        {
            return;
        }
        var list = string.Join("\n", old.Select(b => $"• {Format.DateTime(b.CreatedAt)} ({Format.Bytes(b.SizeBytes)})"));
        if (Dialogs.Confirm(
                $"Hapus permanen {old.Count} backup lama berikut? {BackupRetention.KeepCount} backup terbaru tetap disimpan.\n\n{list}\n\nTidak bisa dikembalikan.",
                dangerous: true))
        {
            await DeleteBackupsAsync(old);
        }
    }

    /// <summary>
    /// Folder backup bisa ratusan MB berisi ribuan file. Penghapusan dijalankan di latar
    /// agar jendela tidak menjadi "Not Responding".
    /// </summary>
    private async Task DeleteBackupsAsync(IReadOnlyList<BackupInfo> backups)
    {
        var store = _workspace.Store!;
        var failures = new List<string>();
        await _workspace.RunExclusiveAsync("Menghapus backup…", () => Task.Run(() =>
        {
            foreach (var backup in backups)
            {
                try
                {
                    store.DeleteBackup(backup);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    failures.Add($"{backup.Name}: {ex.Message}");
                }
            }
        }));

        _workspace.RefreshBackups();
        if (failures.Count > 0)
        {
            Dialogs.Error("Sebagian backup gagal dihapus:\n\n" + string.Join("\n", failures));
        }
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceService.Backups))
        {
            RebuildBackups();
        }
        NotifyState();
    }

    private void RebuildBackups()
    {
        var expanded = Backups.Where(b => b.IsExpanded).Select(b => b.Info.Directory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var old = _workspace.OldBackups.Select(b => b.Directory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Backups.Clear();
        foreach (var backup in _workspace.Backups)
        {
            Backups.Add(new BackupItemViewModel(backup, old.Contains(backup.Directory)) { IsExpanded = expanded.Contains(backup.Directory) });
        }
        OnPropertyChanged(nameof(HasBackups));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(BusyText));
        OnPropertyChanged(nameof(HasScan));
        OnPropertyChanged(nameof(CanModify));
        OnPropertyChanged(nameof(CanMarkHealthy));
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(SnapshotText));
        OnPropertyChanged(nameof(OldBackupCount));
        OnPropertyChanged(nameof(HasOldBackups));
        OnPropertyChanged(nameof(OldBackupText));
        OnPropertyChanged(nameof(StorageError));
        OnPropertyChanged(nameof(DataFolder));
    }
}

public sealed partial class BackupItemViewModel : ObservableObject
{
    public BackupItemViewModel(BackupInfo info, bool isOld)
    {
        Info = info;
        IsOld = isOld;
        Packages = info.Manifest?.Packages.Select(p => new PackageItemViewModel(info, p)).ToList() ?? [];
    }

    public BackupInfo Info { get; }

    public bool IsOld { get; }

    public IReadOnlyList<PackageItemViewModel> Packages { get; }

    [ObservableProperty]
    private bool _isExpanded;

    public string Title => Format.DateTime(Info.CreatedAt);

    public string Subtitle => Info.IsComplete
        ? $"{Info.PackageCount} paket driver · {Format.Bytes(Info.SizeBytes)} · {Format.Age(Info.CreatedAt)}"
        : $"Tidak lengkap (proses backup tidak selesai) · {Format.Bytes(Info.SizeBytes)}";

    public bool IsComplete => Info.IsComplete;

    public string PackagesHeader => $"Lihat {Packages.Count} paket driver";
}

public sealed class PackageItemViewModel
{
    public PackageItemViewModel(BackupInfo backup, BackupPackage package)
    {
        Backup = backup;
        Package = package;
    }

    public BackupInfo Backup { get; }

    public BackupPackage Package { get; }

    public string Name => Package.DisplayName;

    public string Detail => string.Join(" · ", new[]
    {
        Package.Provider,
        Package.Version is null ? null : $"versi {Package.Version}",
        Package.Date is { } d ? Format.Date(d) : null,
        Package.OriginalInfName,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string DevicesText => Package.Devices.Count switch
    {
        0 => "Tidak dipakai perangkat yang tersambung saat backup dibuat",
        1 => "Dipakai 1 perangkat",
        var n => $"Dipakai {n} perangkat: {string.Join(", ", Package.Devices.Select(d => d.Name).Distinct().Take(3))}{(n > 3 ? ", …" : string.Empty)}",
    };
}
