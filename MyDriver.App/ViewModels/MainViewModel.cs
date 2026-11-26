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

    public MainViewModel(AppEnvironment environment, ElevationService elevation, SettingsViewModel settings)
    {
        _elevation = elevation;
        Environment = environment;

        Items = new ObservableCollection<NavigationItem>
        {
            new("Beranda", "", new HomeViewModel(environment)),
            new("Masalah", "", new ProblemsViewModel()),
            new("Perangkat", "", new DevicesViewModel()),
            new("Backup", "", new BackupViewModel()),
            new("Riwayat", "", new HistoryViewModel()),
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
