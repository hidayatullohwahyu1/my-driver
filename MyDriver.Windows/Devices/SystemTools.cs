using System.Diagnostics;
using Microsoft.Win32;
using MyDriver.Core.Logging;
using MyDriver.Windows.Commands;
using MyDriver.Windows.Drivers;

namespace MyDriver.Windows.Devices;

/// <summary>Aksi tingkat sistem: layanan driver, restart Windows, dan membuka System Restore.</summary>
public sealed class SystemTools
{
    private const int StartDemand = 3;
    private const int StartDisabled = 4;

    private readonly ICommandRunner _runner;
    private readonly ILogService _log;

    public SystemTools(ICommandRunner runner, ILogService log)
    {
        _runner = runner;
        _log = log;
    }

    /// <summary>Jenis start layanan dari registry (0 boot, 1 system, 2 otomatis, 3 manual, 4 nonaktif).</summary>
    public static int? GetServiceStartType(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key?.GetValue("Start") as int?;
    }

    /// <summary>Mengaktifkan ulang layanan driver yang dinonaktifkan (start=4 → manual). Butuh Administrator.</summary>
    public async Task<DriverOperationResult> ReenableDriverServiceAsync(string? serviceName, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return new DriverOperationResult(false, $"Nama layanan driver {deviceName} tidak diketahui.");
        }

        var start = GetServiceStartType(serviceName);
        if (start != StartDisabled)
        {
            return new DriverOperationResult(false,
                $"Layanan {serviceName} tidak sedang dinonaktifkan (jenis start {start?.ToString() ?? "tidak diketahui"}). Coba rekomendasi berikutnya.");
        }

        var command = await _runner.RunAsync("sc", ["config", serviceName, "start=", "demand"], TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        return command.Succeeded && GetServiceStartType(serviceName) == StartDemand
            ? new DriverOperationResult(true, $"Layanan driver {deviceName} diaktifkan kembali. Restart Windows agar perangkat memakainya.", command, RebootRequired: true)
            : new DriverOperationResult(false, $"Layanan {serviceName} gagal diaktifkan (kode keluar {command.ExitCode}).", command);
    }

    /// <summary>Restart Windows dalam 15 detik (bisa dibatalkan dengan <c>shutdown /a</c>).</summary>
    public async Task<DriverOperationResult> RestartWindowsAsync()
    {
        var command = await _runner.RunAsync("shutdown",
            ["/r", "/t", "15", "/c", "My Driver!: Windows akan restart dalam 15 detik untuk menyelesaikan perbaikan."],
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return command.Succeeded
            ? new DriverOperationResult(true, "Windows akan restart dalam 15 detik.", command)
            : new DriverOperationResult(false, $"Restart gagal dijadwalkan (kode keluar {command.ExitCode}).", command);
    }

    /// <summary>Membuka jendela System Restore Windows untuk memilih titik pemulihan.</summary>
    public DriverOperationResult OpenSystemRestore()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "rstrui.exe")) { UseShellExecute = true });
            _log.Info("System Restore dibuka.");
            return new DriverOperationResult(true, "Jendela System Restore dibuka. Pilih restore point sebelum masalah muncul.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            _log.Warning("System Restore tidak bisa dibuka.", ex);
            return new DriverOperationResult(false, $"System Restore tidak bisa dibuka: {ex.Message}");
        }
    }
}
