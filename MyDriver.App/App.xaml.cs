using System.Windows;
using System.Windows.Threading;
using MyDriver.App.Services;
using MyDriver.App.ViewModels;
using MyDriver.Core.Logging;
using MyDriver.Core.Settings;
using MyDriver.Core.Storage;
using MyDriver.Windows.Commands;
using MyDriver.Windows.Security;
using MyDriver.Windows.Drivers;
using MyDriver.Windows.Recovery;
using MyDriver.Windows.Wmi;

namespace MyDriver.App;

public partial class App : Application
{
    private ILogService? _log;

    /// <summary>Dipakai fitur tahap berikutnya (pnputil, dism) lewat ViewModel.</summary>
    public static ICommandRunner CommandRunner { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Render tanpa GPU. Aplikasi ini dipakai justru saat driver bermasalah, termasuk driver
        // kartu grafis; dengan render GPU, jendela WPF bisa tampil putih kosong.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        DataPaths paths;
        try
        {
            paths = DataPaths.Resolve(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Folder data tidak bisa dibuat.\n\n{ex.Message}",
                "My Driver!", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ILogService log = new FileLogService(paths.LogsDirectory);
        _log = log;
        RegisterExceptionHandlers(log);

        var elevation = new ElevationService(log);
        var environment = new AppEnvironment(paths, elevation.IsAdministrator);
        log.Info(
            $"My Driver! {AppEnvironment.Version} dimulai. Windows {System.Environment.OSVersion.Version}, " +
            $"{(environment.IsAdministrator ? "Administrator" : "mode baca saja")}, folder data: {paths.Root}" +
            (paths.IsFallback ? " (cadangan, folder di samping exe tidak bisa ditulisi)" : string.Empty));

        var settingsStore = new SettingsStore(paths.SettingsFile, log);
        var settings = settingsStore.Load();

        var themeService = new ThemeService(this, log);
        themeService.Apply(settings.Theme);

        CommandRunner = new CommandRunner(log);

        var scan = new ScanCoordinator(new WmiDeviceScanner(new WmiService(log), log), log);
        var workspace = new WorkspaceService(paths, scan, log);
        var backups = new DriverBackupService(new DismService(CommandRunner), new PnpUtilService(CommandRunner), log);
        var actions = new DriverActions(environment, workspace, scan, backups, new RestorePointService(log), log);

        var settingsViewModel = new SettingsViewModel(environment, themeService, settingsStore, settings);
        var window = new MainWindow
        {
            DataContext = new MainViewModel(environment, elevation, scan, workspace, actions, settingsViewModel),
        };
        themeService.Attach(window);
        MainWindow = window;
        window.Show();

        // Scan pertama langsung saat aplikasi dibuka (hanya membaca, aman tanpa admin).
        _ = scan.ScanAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("My Driver! ditutup.");
        base.OnExit(e);
    }

    private void RegisterExceptionHandlers(ILogService log)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("Galat tak tertangani di UI.", args.Exception);
            MessageBox.Show(
                $"Terjadi kesalahan yang tidak terduga. Rinciannya sudah dicatat di folder log.\n\n{args.Exception.Message}",
                "My Driver!", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Error("Galat fatal tak tertangani.", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("Galat di tugas latar yang tidak ditangani.", args.Exception);
            args.SetObserved();
        };
    }
}
