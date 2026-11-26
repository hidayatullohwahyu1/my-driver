using System.IO;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Core.Settings;

namespace MyDriver.App.ViewModels;

public sealed record ThemeOption(AppTheme Value, string Label);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ThemeService _themeService;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;

    public SettingsViewModel(AppEnvironment environment, ThemeService themeService, SettingsStore store, AppSettings settings)
    {
        Environment = environment;
        _themeService = themeService;
        _store = store;
        _settings = settings;
        SelectedTheme = ThemeOptions.First(o => o.Value == settings.Theme);
    }

    public AppEnvironment Environment { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(AppTheme.System, "Ikuti Windows"),
        new(AppTheme.Light, "Terang"),
        new(AppTheme.Dark, "Gelap"),
    ];

    [ObservableProperty]
    private ThemeOption _selectedTheme = null!;

    public string DataFolder => Environment.Paths.Root;

    public string ModeText => Environment.IsAdministrator
        ? "Berjalan sebagai Administrator. Semua aksi perbaikan tersedia."
        : "Mode baca saja. Scan tetap jalan, tetapi aksi perbaikan dinonaktifkan.";

    public string AppVersion => AppEnvironment.Version;

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

    [RelayCommand]
    private void OpenDataFolder() => OpenInExplorer(Environment.Paths.Root);

    [RelayCommand]
    private void OpenLogFolder() => OpenInExplorer(Environment.Paths.LogsDirectory);

    private static void OpenInExplorer(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
