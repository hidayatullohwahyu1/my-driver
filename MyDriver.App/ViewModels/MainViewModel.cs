using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Windows.Security;

namespace MyDriver.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ElevationService _elevation;

    public MainViewModel(
        AppEnvironment environment, ElevationService elevation, ScanCoordinator scan,
        WorkspaceService workspace, DriverActions actions, DiagnosisService diagnosis, RepairService repair,
        SettingsViewModel settings)
    {
        _elevation = elevation;
        Environment = environment;

        var home = new HomeViewModel(environment, scan, workspace, actions, diagnosis);
        home.OpenProblemsRequested += (_, _) => SelectedItem = Items![1];

        Items = new ObservableCollection<NavigationItem>
        {
            new("Beranda", "", home),
            new("Masalah", "", new ProblemsViewModel(diagnosis, workspace, scan, repair)),
            new("Perangkat", "", new DevicesViewModel(scan)),
            new("Backup", "", new BackupViewModel(workspace, scan, actions)),
            new("Riwayat", "", new HistoryViewModel(workspace)),
            new("Pengaturan", "", settings),
        };
        SelectedItem = Items[0];
    }

    public AppEnvironment Environment { get; }

    public ObservableCollection<NavigationItem> Items { get; }

    [ObservableProperty]
    private NavigationItem _selectedItem = null!;

    public string AppVersion => $"Versi {AppEnvironment.Version}";

    public string ModeLabel => Environment.IsAdministrator ? "Administrator" : "Mode baca saja";

    public bool IsReadOnly => Environment.IsReadOnly;

    public bool IsDataFallback => Environment.Paths.IsFallback;

    /// <summary>Windows di bawah target PRD (Windows 10 22H2): aplikasi tetap jalan, dengan peringatan.</summary>
    public bool IsOldWindows => WindowsVersion.IsOlderThanSupported;

    public string OldWindowsMessage => WindowsVersion.SupportsPnpUtilDeviceCommands
        ? $"Windows ini (build {WindowsVersion.Build}) lebih lama dari Windows 10 22H2. Sebagian besar fitur tetap jalan, tetapi belum diuji di versi ini. Disarankan memperbarui Windows."
        : $"Windows ini (build {WindowsVersion.Build}) lebih lama dari Windows 10 versi 2004. Scan, backup, dan diagnosis tetap jalan, tetapi aksi restart/nonaktifkan/uninstall perangkat tidak tersedia. Gunakan Device Manager atau perbarui Windows.";

    public string DataFallbackMessage =>
        $"Folder di samping file exe tidak bisa ditulisi. Data disimpan di {Environment.Paths.Root}.";

    [RelayCommand]
    private void RunAsAdministrator()
    {
        if (_elevation.TryRestartAsAdministrator())
        {
            Application.Current.Shutdown();
        }
    }
}
