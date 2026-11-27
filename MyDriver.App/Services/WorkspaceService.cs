using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyDriver.Core.Backups;
using MyDriver.Core.Devices;
using MyDriver.Core.Logging;
using MyDriver.Core.Snapshots;
using MyDriver.Core.Storage;

namespace MyDriver.App.Services;

/// <summary>
/// Data komputer yang sedang dipakai: folder data, snapshot sehat, daftar backup.
/// Folder komputer baru bisa ditentukan setelah scan pertama (butuh UUID dan nomor seri).
/// Juga menjaga aturan "sekali jalan": hanya satu aksi berat dalam satu waktu.
/// </summary>
public sealed partial class WorkspaceService : ObservableObject
{
    private readonly DataPaths _paths;
    private readonly ScanCoordinator _scan;
    private readonly ILogService _log;

    public WorkspaceService(DataPaths paths, ScanCoordinator scan, ILogService log)
    {
        _paths = paths;
        _scan = scan;
        _log = log;
        _scan.PropertyChanged += OnScanChanged;
    }

    [ObservableProperty]
    private ComputerStore? _store;

    [ObservableProperty]
    private HealthySnapshot? _snapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastBackup))]
    [NotifyPropertyChangedFor(nameof(OldBackups))]
    private IReadOnlyList<BackupInfo> _backups = [];

    [ObservableProperty]
    private bool _needsOnboarding;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _busyText;

    [ObservableProperty]
    private string? _storageError;

    public BackupInfo? LastBackup => Backups.FirstOrDefault(b => b.IsComplete);

    public IReadOnlyList<BackupInfo> OldBackups => BackupRetention.OldBackups(Backups);

    public void SaveSnapshot(ScanResult scan)
    {
        if (Store is null)
        {
            return;
        }
        var snapshot = HealthySnapshot.FromScan(scan, DateTime.Now);
        Store.SaveSnapshot(snapshot);
        Snapshot = snapshot;
    }

    public void CompleteOnboarding()
    {
        if (Store is null)
        {
            return;
        }
        Store.Profile.OnboardingCompleted = true;
        Store.SaveProfile();
        NeedsOnboarding = false;
    }

    public void RefreshBackups() => Backups = Store?.ListBackups() ?? [];

    /// <summary>Menjalankan satu aksi berat. Mengembalikan false bila ada aksi lain yang masih berjalan.</summary>
    public async Task<bool> RunExclusiveAsync(string busyText, Func<Task> action)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        BusyText = busyText;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
        return true;
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScanCoordinator.LastResult) || _scan.LastResult is not { } result)
        {
            return;
        }

        try
        {
            if (Store is null)
            {
                Store = ComputerStore.Open(_paths, result.Computer, DateTime.Now, _log);
                Snapshot = Store.LoadSnapshot();
                RefreshBackups();
                NeedsOnboarding = !Store.Profile.OnboardingCompleted && Snapshot is null;
            }
            Store.SaveScan(result);
            StorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Folder data komputer tidak bisa ditulisi.", ex);
            StorageError = $"Folder data tidak bisa ditulisi: {ex.Message}";
        }
    }
}
