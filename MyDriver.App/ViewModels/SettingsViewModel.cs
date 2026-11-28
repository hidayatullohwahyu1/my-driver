using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Core.Settings;

namespace MyDriver.App.ViewModels;

public sealed record ThemeOption(AppTheme Value, string Label);

public sealed record HourOption(int Hour)
{
    public string Label => $"{Hour:00}.00";
}

/// <summary>
/// Pengaturan: pemantauan otomatis (tugas terjadwal), jam scan harian, notifikasi, tema,
/// dan sembunyikan perangkat tidak tersambung.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ThemeService _themeService;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly MonitoringService _monitoring;
    private bool _loading = true;

    public SettingsViewModel(
        AppEnvironment environment, ThemeService themeService, SettingsStore store, AppSettings settings, MonitoringService monitoring)
    {
        Environment = environment;
        _themeService = themeService;
        _store = store;
        _settings = settings;
        _monitoring = monitoring;
        SelectedTheme = ThemeOptions.First(o => o.Value == settings.Theme);
        SelectedHour = HourOptions.First(o => o.Hour == Math.Clamp(settings.DailyScanHour, 0, 23));
        NotificationsEnabled = settings.NotificationsEnabled;
        HideDisconnectedDevices = settings.HideDisconnectedDevices;
        _loading = false;
        _ = RefreshMonitoringAsync();
    }

    public AppEnvironment Environment { get; }

    public AppSettings Settings => _settings;

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(AppTheme.System, "Ikuti Windows"),
        new(AppTheme.Light, "Terang"),
        new(AppTheme.Dark, "Gelap"),
    ];

    public IReadOnlyList<HourOption> HourOptions { get; } = Enumerable.Range(0, 24).Select(h => new HourOption(h)).ToList();

    [ObservableProperty]
    private ThemeOption _selectedTheme = null!;

    [ObservableProperty]
    private HourOption _selectedHour = null!;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private bool _hideDisconnectedDevices;

    /// <summary>Status sebenarnya dari Task Scheduler.</summary>
    [ObservableProperty]
    private bool _isMonitoringEnabled;

    [ObservableProperty]
    private bool _isMonitoringBusy = true;

    [ObservableProperty]
    private string _monitoringText = "Memeriksa status pemantauan…";

    public bool CanChangeMonitoring => !IsMonitoringBusy;

    public bool IsOnRemovableDrive => MonitoringService.IsOnRemovableDrive;

    public string DataFolder => Environment.Paths.Root;

    public string ModeText => Environment.IsAdministrator
        ? "Berjalan sebagai Administrator. Semua aksi perbaikan tersedia."
        : "Mode baca saja. Scan tetap jalan, tetapi aksi perbaikan dinonaktifkan.";

    public string AppVersion => AppEnvironment.Version;

    /// <summary>Dipakai Beranda (alur pertama kali) untuk menyalakan pemantauan.</summary>
    public async Task<bool> EnableMonitoringAsync()
    {
        IsMonitoringBusy = true;
        try
        {
            var result = await _monitoring.EnableAsync(_settings.DailyScanHour);
            if (result is null)
            {
                return false;
            }
            if (result.Success)
            {
                Dialogs.Info(result.Message);
            }
            else
            {
                Dialogs.Error(result.Message);
            }
            return result.Success;
        }
        finally
        {
            await RefreshMonitoringAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleMonitoringAsync()
    {
        if (IsMonitoringEnabled)
        {
            IsMonitoringBusy = true;
            var result = await _monitoring.DisableAsync();
            if (result is { Success: false })
            {
                Dialogs.Error(result.Message);
            }
            await RefreshMonitoringAsync();
            return;
        }

        if (IsOnRemovableDrive && !Dialogs.Confirm(
                "My Driver! sedang dijalankan dari flashdisk. Pemantauan otomatis memakai lokasi exe ini, " +
                "jadi scan otomatis tidak jalan saat flashdisk dicabut.\n\nTetap aktifkan? (Lebih baik salin My Driver! ke komputer ini dulu.)"))
        {
            return;
        }
        await EnableMonitoringAsync();
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenInExplorer(Environment.Paths.Root);

    [RelayCommand]
    private void OpenLogFolder() => OpenInExplorer(Environment.Paths.LogsDirectory);

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (value is null || value.Value == _settings.Theme && value.Value == _themeService.Theme)
        {
            return;
        }
        _settings.Theme = value.Value;
        _themeService.Apply(value.Value);
        _store.Save(_settings);
    }

    partial void OnSelectedHourChanged(HourOption value)
    {
        if (_loading || value is null || value.Hour == _settings.DailyScanHour)
        {
            return;
        }
        _settings.DailyScanHour = value.Hour;
        _store.Save(_settings);
        if (IsMonitoringEnabled)
        {
            _ = EnableMonitoringAsync(); // buat ulang tugas dengan jam baru
        }
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }
        _settings.NotificationsEnabled = value;
        _store.Save(_settings);
    }

    partial void OnHideDisconnectedDevicesChanged(bool value)
    {
        if (_loading)
        {
            return;
        }
        _settings.HideDisconnectedDevices = value;
        _store.Save(_settings);
    }

    partial void OnIsMonitoringBusyChanged(bool value) => OnPropertyChanged(nameof(CanChangeMonitoring));

    private async Task RefreshMonitoringAsync()
    {
        IsMonitoringBusy = true;
        try
        {
            var path = await _monitoring.GetTaskExePathAsync();
            IsMonitoringEnabled = path is not null;
            MonitoringText = path switch
            {
                null => "Mati. Aktifkan agar komputer diperiksa saat Windows dibuka dan sekali sehari, lalu diberi tahu bila ada masalah baru.",
                _ when string.Equals(path, MonitoringService.ExePath, StringComparison.OrdinalIgnoreCase) =>
                    $"Aktif: scan saat Windows dibuka dan setiap hari pukul {_settings.DailyScanHour:00}.00.",
                _ => $"Aktif, tetapi memakai exe di lokasi lain: {path}. Matikan lalu aktifkan lagi agar memakai lokasi sekarang.",
            };
        }
        finally
        {
            IsMonitoringBusy = false;
        }
    }

    private static void OpenInExplorer(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
