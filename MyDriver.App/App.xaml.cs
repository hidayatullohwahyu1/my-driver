using System.Globalization;
using System.Windows;
using MyDriver.App.Services;
using MyDriver.App.ViewModels;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Logging;
using MyDriver.Core.Reports;
using MyDriver.Core.Settings;
using MyDriver.Core.Storage;
using MyDriver.Windows.Commands;
using MyDriver.Windows.Devices;
using MyDriver.Windows.Drivers;
using MyDriver.Windows.Health;
using MyDriver.Windows.Monitoring;
using MyDriver.Windows.Recovery;
using MyDriver.Windows.Security;
using MyDriver.Windows.Update;
using MyDriver.Windows.Wmi;

namespace MyDriver.App;

public partial class App : Application
{
    private ILogService? _log;
    private TrayService? _tray;

    /// <summary>Scan latar dari Task Scheduler: tanpa jendela dan tanpa kotak dialog.</summary>
    private bool _background;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _background = e.Args.Any(a => string.Equals(a, ScheduledTaskService.BackgroundArgument, StringComparison.OrdinalIgnoreCase));
        var openProblems = e.Args.Any(a => string.Equals(a, BackgroundMonitor.OpenProblemsArgument, StringComparison.OrdinalIgnoreCase));

        SplashScreen? splash = null;
        if (!_background)
        {
            splash = new SplashScreen("Assets/splash.png");
            splash.Show(autoClose: true, topMost: false);
        }

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
            if (!_background)
            {
                MessageBox.Show($"Folder data tidak bisa dibuat.\n\n{ex.Message}", "My Driver!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(1);
            return;
        }

        ILogService log = new FileLogService(paths.LogsDirectory);
        _log = log;
        RegisterExceptionHandlers(log);
        BindingErrorListener.Attach(log);

        if (_background)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunBackgroundAsync(paths, log);
            return;
        }

        // Galat saat menyusun aplikasi dulu membuat aplikasi tertutup diam-diam.
        // Sekarang dicatat ke log dan ditampilkan ke pengguna.
        try
        {
            Start(paths, log, openProblems);
        }
        catch (Exception ex)
        {
            log.Error("Aplikasi gagal dibuka.", ex);
            splash?.Close(TimeSpan.Zero);
            MessageBox.Show(
                $"My Driver! gagal dibuka di Windows ini.\n\n{ex.Message}\n\nRincian tercatat di:\n{paths.LogsDirectory}",
                "My Driver!", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task RunBackgroundAsync(DataPaths paths, ILogService log)
    {
        try
        {
            var settings = new SettingsStore(paths.SettingsFile, log).Load();
            await BackgroundMonitor.RunAsync(paths, log, settings);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            log.Error("Scan otomatis gagal.", ex);
            Shutdown(1);
        }
    }

    private void Start(DataPaths paths, ILogService log, bool openProblems)
    {
        var elevation = new ElevationService(log);
        var environment = new AppEnvironment(paths, elevation.IsAdministrator);
        log.Info(
            $"My Driver! {AppEnvironment.Version} dimulai. Windows {System.Environment.OSVersion.Version} " +
            $"({(System.Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}, bahasa {CultureInfo.CurrentUICulture.Name}, " +
            $"layar {SystemParameters.PrimaryScreenWidth:0}x{SystemParameters.PrimaryScreenHeight:0}), " +
            $"{(environment.IsAdministrator ? "Administrator" : "mode baca saja")}, folder data: {paths.Root}" +
            (paths.IsFallback ? " (cadangan, folder di samping exe tidak bisa ditulisi)" : string.Empty));
        if (WindowsVersion.IsOlderThanSupported)
        {
            log.Warning($"Windows build {WindowsVersion.Build} lebih lama dari target (Windows 10 22H2, build {WindowsVersion.Windows10_22H2}).");
        }

        var settingsStore = new SettingsStore(paths.SettingsFile, log);
        var settings = settingsStore.Load();

        var themeService = new ThemeService(this, log);
        themeService.Apply(settings.Theme);

        ICommandRunner runner = new CommandRunner(log);
        var scan = new ScanCoordinator(new WmiDeviceScanner(new WmiService(log), log), log,
            new SystemHealthReader(new EventLogService(log), log));
        var workspace = new WorkspaceService(paths, scan, log);
        var backups = new DriverBackupService(new DismService(runner), new PnpUtilService(runner), log);
        var actions = new DriverActions(environment, workspace, scan, backups, new RestorePointService(log), elevation, log);
        var diagnosis = new DiagnosisService(RuleSet.Load(paths.RulesFile, log), scan, workspace, log);
        var repair = new RepairService(workspace, scan, actions,
            new DeviceControlService(runner), new DriverInstaller(log), new SystemTools(runner, log),
            new WindowsUpdateService(log), log);
        var monitoring = new MonitoringService(new ScheduledTaskService(runner), workspace, actions, log);
        var driverStore = new DriverStoreInventory(runner, log);

        var settingsViewModel = new SettingsViewModel(environment, themeService, settingsStore, settings, monitoring);
        var mainViewModel = new MainViewModel(
            environment, elevation, scan, workspace, actions, diagnosis, repair, monitoring, driverStore, settings, settingsViewModel);
        if (openProblems)
        {
            mainViewModel.OpenProblems();
        }

        var window = new MainWindow { DataContext = mainViewModel };
        themeService.Attach(window);
        MainWindow = window;
        window.Show();

        SetUpTray(window, scan, diagnosis);

        // Scan pertama langsung saat aplikasi dibuka (hanya membaca, aman tanpa admin).
        _ = scan.ScanAsync();
    }

    /// <summary>Ikon tray selama aplikasi terbuka: status di tooltip, menu buka/scan/keluar.</summary>
    private void SetUpTray(Window window, ScanCoordinator scan, DiagnosisService diagnosis)
    {
        _tray = new TrayService(withMenu: true);
        _tray.OpenRequested += (_, _) =>
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
        };
        _tray.ScanRequested += (_, _) => _ = scan.ScanAsync();
        _tray.ExitRequested += (_, _) => Shutdown();
        diagnosis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiagnosisService.Report) && diagnosis.Report is { } report)
            {
                _tray.SetStatus($"My Driver!: {ReportBuilder.StatusText(report.Status)}");
            }
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _log?.Info(_background ? "Scan otomatis ditutup." : "My Driver! ditutup.");
        base.OnExit(e);
    }

    private void RegisterExceptionHandlers(ILogService log)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("Galat tak tertangani di UI.", args.Exception);
            if (!_background)
            {
                MessageBox.Show(
                    $"Terjadi kesalahan yang tidak terduga. Rinciannya sudah dicatat di folder log.\n\n{args.Exception.Message}",
                    "My Driver!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
