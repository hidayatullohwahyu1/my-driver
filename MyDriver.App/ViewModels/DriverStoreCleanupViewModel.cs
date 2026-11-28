using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Core.Repair;
using MyDriver.Windows.Drivers;

namespace MyDriver.App.ViewModels;

/// <summary>
/// Pembersihan DriverStore (F10): paket driver lama yang tidak dipakai beserta ukurannya;
/// hapus yang dipilih dengan konfirmasi. Paket yang masih dipakai tidak pernah ditawarkan.
/// </summary>
public sealed partial class DriverStoreCleanupViewModel : ObservableObject
{
    private readonly DriverStoreInventory _inventory;
    private readonly WorkspaceService _workspace;
    private readonly ScanCoordinator _scan;
    private readonly DriverActions _actions;

    public DriverStoreCleanupViewModel(DriverStoreInventory inventory, WorkspaceService workspace, ScanCoordinator scan, DriverActions actions)
    {
        _inventory = inventory;
        _workspace = workspace;
        _scan = scan;
        _actions = actions;
    }

    public ObservableCollection<StorePackageItem> Packages { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasChecked;

    [ObservableProperty]
    private string _summaryText = "Belum diperiksa.";

    public bool HasPackages => Packages.Count > 0;

    public string SelectionText
    {
        get
        {
            var selected = Packages.Where(p => p.IsSelected).ToList();
            return selected.Count == 0
                ? "Belum ada yang dipilih."
                : $"{selected.Count} paket dipilih · {Format.Bytes(selected.Sum(p => p.Package.SizeBytes))} akan dibebaskan";
        }
    }

    public bool CanDelete => Packages.Any(p => p.IsSelected) && !IsLoading && !_workspace.IsBusy;

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (_scan.LastResult is not { } scan)
        {
            Dialogs.Info("Tunggu sampai scan perangkat selesai.");
            return;
        }

        IsLoading = true;
        try
        {
            var all = await _inventory.ListAsync(scan.Devices);
            foreach (var old in Packages)
            {
                old.PropertyChanged -= OnItemChanged;
            }
            Packages.Clear();
            foreach (var package in all.Where(p => p.CanOfferForRemoval))
            {
                var item = new StorePackageItem(package) { IsSelected = package.IsOlderVersion };
                item.PropertyChanged += OnItemChanged;
                Packages.Add(item);
            }

            var used = all.Count(p => p.InUse);
            SummaryText = Packages.Count == 0
                ? $"Bersih: {all.Count} paket driver pihak ketiga, semuanya dipakai atau dilindungi."
                : $"{Packages.Count} paket tidak dipakai ({Format.Bytes(Packages.Sum(p => p.Package.SizeBytes))}) dari {all.Count} paket; {used} sedang dipakai.";
            HasChecked = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"DriverStore tidak bisa dibaca: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            NotifySelection();
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = Packages.Where(p => p.IsSelected).Select(p => p.Package).ToList();
        if (selected.Count == 0 || !_actions.CheckAdministrator("Menghapus paket driver"))
        {
            return;
        }

        var list = string.Join("\n", selected.Take(12).Select(p => $"• {p.OriginalName} {p.Version} ({p.Provider}, {Format.Bytes(p.SizeBytes)})"));
        if (selected.Count > 12)
        {
            list += $"\n• … dan {selected.Count - 12} lainnya";
        }
        var backupNote = _workspace.LastBackup is null
            ? "\n\nBelum ada backup driver. Paket yang dihapus tidak bisa dipasang lagi tanpa backup atau unduhan ulang."
            : string.Empty;
        if (!Dialogs.Confirm(
                $"Hapus {selected.Count} paket driver yang tidak dipakai ({Format.Bytes(selected.Sum(p => p.SizeBytes))})?\n\n{list}{backupNote}\n\n" +
                "Windows menolak menghapus paket yang ternyata masih dipakai. Restore point dibuat lebih dulu.",
                dangerous: true))
        {
            return;
        }

        var started = await _workspace.RunExclusiveAsync("Menghapus paket driver lama…", async () =>
        {
            var restorePoint = await _actions.EnsureRestorePointAsync("My Driver!: bersihkan DriverStore");
            if (restorePoint is null)
            {
                return;
            }

            var (deleted, failures, commands) = await _inventory.DeleteAsync(selected);
            var message = failures.Count == 0
                ? $"{deleted} paket driver dihapus."
                : $"{deleted} paket dihapus, {failures.Count} gagal (mungkin masih dipakai):\n{string.Join("\n", failures)}";
            _workspace.AddHistory(new HistoryEntry
            {
                Time = DateTime.Now,
                Kind = HistoryKind.Action,
                Title = "Bersihkan DriverStore",
                Success = failures.Count == 0,
                Message = message,
                RestorePoint = restorePoint,
                Commands = commands,
            });
            if (failures.Count == 0)
            {
                Dialogs.Info(message);
            }
            else
            {
                Dialogs.Warning(message);
            }
        });
        DriverActions.NotifyIfBusy(started);

        if (started)
        {
            await CheckAsync();
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StorePackageItem.IsSelected))
        {
            NotifySelection();
        }
    }

    partial void OnIsLoadingChanged(bool value) => NotifySelection();

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(HasPackages));
    }
}

public sealed partial class StorePackageItem : ObservableObject
{
    public StorePackageItem(DriverStorePackage package)
    {
        Package = package;
    }

    public DriverStorePackage Package { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Title => string.Join(" · ", new[] { Package.Provider, Package.Class, Package.OriginalName }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Detail => string.Join(" · ", new[]
    {
        Package.Version is null ? null : $"versi {Package.Version}",
        Format.Bytes(Package.SizeBytes),
        Package.PublishedName,
        Package.IsOlderVersion ? "versi lama dari driver yang sedang dipakai" : "tidak dipakai perangkat yang tersambung",
    }.Where(s => s is not null));
}
