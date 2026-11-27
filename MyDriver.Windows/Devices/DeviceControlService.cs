using MyDriver.Windows.Commands;
using MyDriver.Windows.Drivers;
using MyDriver.Windows.Security;

namespace MyDriver.Windows.Devices;

/// <summary>
/// Aksi perangkat lewat pnputil: restart, nonaktifkan/aktifkan, hapus, dan scan ulang hardware.
/// Butuh Administrator dan Windows 10 versi 2004 (build 19041) atau lebih baru.
/// </summary>
public sealed class DeviceControlService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private readonly ICommandRunner _runner;
    private readonly Func<bool> _supported;

    public DeviceControlService(ICommandRunner runner, Func<bool>? supported = null)
    {
        _runner = runner;
        _supported = supported ?? (() => WindowsVersion.SupportsPnpUtilDeviceCommands);
    }

    public Task<DriverOperationResult> RestartAsync(string instanceId, string name) =>
        RunAsync(["/restart-device", instanceId], $"{name} berhasil di-restart.");

    public Task<DriverOperationResult> EnableAsync(string instanceId, string name) =>
        RunAsync(["/enable-device", instanceId], $"{name} berhasil diaktifkan.");

    public Task<DriverOperationResult> DisableAsync(string instanceId, string name) =>
        RunAsync(["/disable-device", instanceId], $"{name} dinonaktifkan.");

    /// <summary>Nonaktifkan lalu aktifkan lagi. Bila mengaktifkan gagal, dicoba sekali lagi.</summary>
    public async Task<DriverOperationResult> DisableThenEnableAsync(string instanceId, string name)
    {
        var disabled = await DisableAsync(instanceId, name).ConfigureAwait(false);
        if (!disabled.Success)
        {
            return disabled;
        }

        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var enabled = await EnableAsync(instanceId, name).ConfigureAwait(false);
        if (!enabled.Success)
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            enabled = await EnableAsync(instanceId, name).ConfigureAwait(false);
        }
        return enabled.Success
            ? enabled with { Message = $"{name} sudah dinonaktifkan lalu diaktifkan kembali." }
            : enabled with { Message = $"{name} sudah dinonaktifkan tetapi GAGAL diaktifkan lagi. Aktifkan lewat Device Manager. {enabled.Message}" };
    }

    /// <summary>Menghapus perangkat (drivernya tetap ada di DriverStore), lalu scan ulang agar Windows memasangnya lagi.</summary>
    public async Task<DriverOperationResult> RemoveAndRescanAsync(string instanceId, string name)
    {
        var removed = await RunAsync(["/remove-device", instanceId], $"{name} dihapus.").ConfigureAwait(false);
        if (!removed.Success)
        {
            return removed;
        }

        var rescan = await RescanAsync().ConfigureAwait(false);
        return rescan.Success
            ? rescan with { Message = $"{name} sudah di-uninstall lalu dipasang ulang oleh Windows.", RebootRequired = removed.RebootRequired || rescan.RebootRequired }
            : rescan with { Message = $"{name} sudah di-uninstall, tetapi scan ulang gagal. Tekan \"Scan ulang hardware\" atau restart Windows. {rescan.Message}" };
    }

    public Task<DriverOperationResult> RescanAsync() =>
        RunAsync(["/scan-devices"], "Windows selesai mendeteksi ulang perangkat.");

    internal async Task<DriverOperationResult> RunAsync(IReadOnlyList<string> arguments, string successMessage)
    {
        if (!_supported())
        {
            return new DriverOperationResult(false,
                $"Aksi ini butuh Windows 10 versi 2004 atau lebih baru (Windows ini build {WindowsVersion.Build}). Gunakan Device Manager.");
        }

        var command = await _runner.RunAsync("pnputil", arguments, Timeout).ConfigureAwait(false);
        return PnpUtilService.Interpret(command) switch
        {
            PnpUtilOutcome.Success or PnpUtilOutcome.NoChange => new(true, successMessage, command),
            PnpUtilOutcome.SuccessRebootRequired => new(true, $"{successMessage} Restart Windows untuk menyelesaikan.", command, RebootRequired: true),
            _ => new(false, $"Gagal (kode keluar {command.ExitCode}). {LastLine(command)}", command),
        };
    }

    private static string LastLine(CommandResult command) =>
        (command.StandardError + "\n" + command.StandardOutput)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;
}
