using System.Diagnostics;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Logging;
using MyDriver.Core.Monitoring;
using MyDriver.Core.Repair;
using MyDriver.Core.Reports;
using MyDriver.Core.Settings;
using MyDriver.Core.Storage;
using MyDriver.Windows.Health;
using MyDriver.Windows.Wmi;

namespace MyDriver.App.Services;

/// <summary>
/// Scan otomatis (F7) yang dijalankan Task Scheduler dengan <c>MyDriver.exe --background</c>:
/// tanpa jendela, hanya membaca, lalu memunculkan notifikasi bila ada masalah kuning/merah yang baru.
/// Klik notifikasi membuka aplikasi di halaman Masalah.
/// </summary>
public static class BackgroundMonitor
{
    public const string OpenProblemsArgument = "--open=problems";

    private static readonly TimeSpan NotificationWait = TimeSpan.FromSeconds(60);

    public static async Task RunAsync(DataPaths paths, ILogService log, AppSettings settings)
    {
        // Hanya satu scan latar dalam satu waktu (logon dan jadwal harian bisa berdekatan).
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MyDriver.BackgroundScan", out var acquired);
        if (!acquired)
        {
            log.Info("Scan otomatis dilewati: scan latar lain masih berjalan.");
            return;
        }

        log.Info("Scan otomatis dimulai.");
        var now = DateTime.Now;
        var scanner = new WmiDeviceScanner(new WmiService(log), log);
        var scan = await scanner.ScanAsync();
        try
        {
            scan = await scanner.AddSignaturesAsync(scan);
        }
        catch (Exception ex)
        {
            log.Warning("Pemeriksaan tanda tangan driver dilewati.", ex);
        }
        var health = await new SystemHealthReader(new EventLogService(log), log).ReadAsync(scan.Devices.Select(d => d.Service));

        var store = ComputerStore.Open(paths, scan.Computer, now, log);
        store.SaveScan(scan);
        var report = new DiagnosisEngine(RuleSet.Load(paths.RulesFile, log)).Diagnose(new DiagnosisInput(
            scan, store.LoadSnapshot(), store.ListBackups().FirstOrDefault(b => b.IsComplete)?.CreatedAt,
            now, store.LoadFindingsSeen(), health));
        store.SaveFindingsSeen(report.FirstSeenMap());

        var decision = MonitorPolicy.Decide(report, store.LoadMonitorState(), now);
        store.SaveMonitorState(decision.NewState);
        store.AppendHistory(new HistoryEntry
        {
            Time = now,
            Kind = HistoryKind.Scan,
            Title = "Scan otomatis",
            Message = $"Status: {ReportBuilder.StatusText(report.Status)}. {scan.ConnectedCount} perangkat, " +
                      $"{report.CriticalCount} merah, {report.WarningCount} kuning" +
                      (decision.NewFindings.Count > 0 ? $", {decision.NewFindings.Count} temuan baru." : "."),
        });
        log.Info($"Scan otomatis selesai: {report.Status}, {decision.NewFindings.Count} temuan baru, notifikasi {(decision.Notify && settings.NotificationsEnabled ? "ya" : "tidak")}.");

        if (decision.Notify && settings.NotificationsEnabled)
        {
            await NotifyAsync(decision, log);
        }
    }

    private static async Task NotifyAsync(MonitorDecision decision, ILogService log)
    {
        using var tray = new TrayService(withMenu: false);
        var result = new TaskCompletionSource<bool>();
        var shownAt = DateTime.Now;
        tray.NotificationClicked += (_, _) => result.TrySetResult(true);
        // Windows kadang langsung mengirim "ditutup" begitu notifikasi tampil; bila proses ikut keluar,
        // ikon tray hilang dan notifikasinya ikut lenyap. Hanya penutupan oleh pengguna (> 3 detik) yang dihitung.
        tray.NotificationClosed += (_, _) =>
        {
            if (DateTime.Now - shownAt > TimeSpan.FromSeconds(3))
            {
                result.TrySetResult(false);
            }
        };
        tray.ShowNotification(decision.Title!, decision.Message!, warning: true);
        log.Info($"Notifikasi ditampilkan: {decision.Message}");

        var clicked = await Task.WhenAny(result.Task, Task.Delay(NotificationWait)) == result.Task && result.Task.Result;
        if (!clicked)
        {
            return;
        }

        log.Info("Notifikasi diklik; membuka halaman Masalah.");
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, OpenProblemsArgument) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log.Warning("Aplikasi tidak bisa dibuka dari notifikasi.", ex);
        }
    }
}
