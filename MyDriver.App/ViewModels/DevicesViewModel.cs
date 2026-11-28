using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Core.Devices;
using MyDriver.Core.Settings;

namespace MyDriver.App.ViewModels;

/// <summary>Halaman Perangkat: pohon perangkat per kategori, detail driver, dan info komputer.</summary>
public sealed partial class DevicesViewModel : ObservableObject
{
    private readonly ScanCoordinator _scan;
    private readonly Dictionary<DeviceCategory, bool> _expanded = new();
    private CancellationTokenSource? _feedbackCts;

    public DevicesViewModel(ScanCoordinator scan, AppSettings settings)
    {
        _scan = scan;
        _showDisconnected = !settings.HideDisconnectedDevices;
        _scan.PropertyChanged += OnScanChanged;
        Rebuild();
    }

    public ObservableCollection<CategoryGroupViewModel> Groups { get; } = [];

    public ComputerInfo? Computer => _scan.LastResult?.Computer;

    public bool HasComputer => Computer is not null;

    public bool HasResult => _scan.LastResult is not null;

    /// <summary>Kotak centang "tampilkan tidak tersambung" hanya muncul bila memang ada perangkatnya.</summary>
    public bool HasDisconnected => _scan.LastResult?.Devices.Any(d => d.Status == DeviceStatus.NotConnected) == true;

    public bool IsScanning => _scan.IsScanning;

    public bool IsFirstScanRunning => IsScanning && !HasResult;

    public string? ScanError => _scan.LastError;

    public bool HasScanError => ScanError is not null;

    public bool IsEmpty => HasResult && Groups.Count == 0;

    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>Bawaan: perangkat tak tersambung (Kode 45) disembunyikan.</summary>
    [ObservableProperty]
    private bool _showDisconnected;

    [ObservableProperty]
    private DeviceItemViewModel? _selectedDevice;

    [ObservableProperty]
    private string? _feedback;

    public string SummaryText
    {
        get
        {
            var result = _scan.LastResult;
            if (result is null)
            {
                return IsScanning ? "Sedang membaca perangkat…" : "Belum dipindai.";
            }

            var parts = new List<string> { $"{result.ConnectedCount} perangkat tersambung" };
            if (result.ProblemCount > 0)
            {
                parts.Add($"{result.ProblemCount} bermasalah");
            }
            if (result.DisabledCount > 0)
            {
                parts.Add($"{result.DisabledCount} dinonaktifkan");
            }
            var hidden = result.Devices.Count - result.ConnectedCount;
            if (hidden > 0)
            {
                parts.Add(ShowDisconnected ? $"{hidden} tidak tersambung" : $"{hidden} tidak tersambung disembunyikan");
            }
            parts.Add($"dipindai {result.ScannedAt:HH.mm} ({result.Duration.TotalSeconds:0.0} dtk)");
            if (_scan.IsCheckingSignatures)
            {
                parts.Add("memeriksa tanda tangan driver…");
            }
            return string.Join(" · ", parts);
        }
    }

    public bool HasSelection => SelectedDevice is not null;

    [RelayCommand]
    private Task ScanAsync() => _scan.ScanAsync();

    [RelayCommand]
    private void CopyComputerInfo()
    {
        if (Computer is { } computer)
        {
            Copy(computer.ToClipboardText(), "Info komputer disalin.");
        }
    }

    [RelayCommand]
    private void CopyHardwareIds()
    {
        if (SelectedDevice is { HasHardwareIds: true } device)
        {
            Copy(string.Join(Environment.NewLine, device.HardwareIds), "Hardware ID disalin.");
        }
    }

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Copy(text, "Disalin.");
        }
    }

    /// <summary>Dipanggil View saat pilihan di pohon berubah (TreeView tidak mendukung binding SelectedItem).</summary>
    public void OnTreeSelectionChanged(object? node) =>
        SelectedDevice = node as DeviceItemViewModel;

    partial void OnFilterTextChanged(string value) => Rebuild();

    partial void OnShowDisconnectedChanged(bool value)
    {
        Rebuild();
        OnPropertyChanged(nameof(SummaryText));
    }

    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ScanCoordinator.LastResult):
                Rebuild();
                OnPropertyChanged(nameof(Computer));
                OnPropertyChanged(nameof(HasComputer));
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(HasDisconnected));
                break;
            case nameof(ScanCoordinator.IsScanning):
                OnPropertyChanged(nameof(IsScanning));
                break;
            case nameof(ScanCoordinator.LastError):
                OnPropertyChanged(nameof(ScanError));
                OnPropertyChanged(nameof(HasScanError));
                break;
        }
        OnPropertyChanged(nameof(IsFirstScanRunning));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void Rebuild()
    {
        foreach (var old in Groups)
        {
            _expanded[old.Category] = old.IsExpanded;
        }
        var selectedId = SelectedDevice?.Device.InstanceId;

        Groups.Clear();
        var devices = _scan.LastResult?.Devices ?? [];
        var filter = FilterText.Trim();
        DeviceItemViewModel? reselect = null;

        foreach (var category in DeviceCategories.All)
        {
            var items = devices
                .Where(d => d.Category == category)
                .Where(d => ShowDisconnected || d.Status != DeviceStatus.NotConnected)
                .Select(d => new DeviceItemViewModel(d, _scan.LastResult!.SignaturesChecked))
                .Where(d => filter.Length == 0 || d.Matches(filter))
                .ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var group = new CategoryGroupViewModel(category, items);
            group.IsExpanded = filter.Length > 0 || group.HasProblems
                || (_expanded.TryGetValue(category, out var wasExpanded) ? wasExpanded : category != DeviceCategory.Other);
            Groups.Add(group);

            reselect ??= items.FirstOrDefault(i => i.Device.InstanceId == selectedId);
        }

        SelectedDevice = reselect;
        if (reselect is not null)
        {
            reselect.IsSelected = true;
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async void Copy(string text, string message)
    {
        Feedback = ClipboardHelper.TrySetText(text) ? message : "Clipboard sedang dipakai aplikasi lain. Coba lagi.";

        _feedbackCts?.Cancel();
        var cts = _feedbackCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(2500, cts.Token);
            Feedback = null;
        }
        catch (TaskCanceledException)
        {
        }
    }
}
