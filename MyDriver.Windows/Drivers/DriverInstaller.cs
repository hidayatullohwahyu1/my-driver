using System.ComponentModel;
using System.Runtime.InteropServices;
using MyDriver.Core.Logging;

namespace MyDriver.Windows.Drivers;

/// <summary>
/// Memasang file INF tertentu ke perangkat walau Windows menganggap driver lain lebih cocok
/// (seperti "Roll Back Driver" di Device Manager). Dipakai untuk "kembalikan ke versi snapshot"
/// dan "pasang dari backup" pada satu perangkat. Butuh Administrator.
/// </summary>
public sealed class DriverInstaller
{
    private const uint InstallFlagForce = 0x1;
    private const uint InstallFlagNonInteractive = 0x4;
    private const int ErrorNoSuchDevinst = unchecked((int)0xE000020B);
    private const int ErrorNoDriverSelected = unchecked((int)0xE0000203);

    private readonly ILogService _log;

    public DriverInstaller(ILogService log)
    {
        _log = log;
    }

    /// <summary>
    /// Mencoba setiap Hardware ID perangkat (dari yang paling spesifik) sampai INF cocok.
    /// </summary>
    public Task<DriverOperationResult> ForceInstallAsync(IReadOnlyList<string> hardwareIds, string infPath, string deviceName) =>
        Task.Run(() => ForceInstall(hardwareIds, infPath, deviceName));

    private DriverOperationResult ForceInstall(IReadOnlyList<string> hardwareIds, string infPath, string deviceName)
    {
        if (!File.Exists(infPath))
        {
            return new DriverOperationResult(false, $"File driver tidak ditemukan: {infPath}");
        }
        if (hardwareIds.Count == 0)
        {
            return new DriverOperationResult(false, $"{deviceName} tidak punya Hardware ID, jadi driver tidak bisa dipasang paksa.");
        }

        var lastError = 0;
        foreach (var id in hardwareIds)
        {
            _log.Info($"Memasang paksa {infPath} untuk Hardware ID {id}");
            if (UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, id, infPath, InstallFlagForce | InstallFlagNonInteractive, out var reboot))
            {
                _log.Info($"Berhasil{(reboot ? " (perlu restart)" : string.Empty)}.");
                return new DriverOperationResult(true,
                    reboot ? $"Driver {deviceName} berhasil dipasang. Restart Windows untuk menyelesaikan." : $"Driver {deviceName} berhasil dipasang.",
                    RebootRequired: reboot);
            }

            lastError = Marshal.GetLastWin32Error();
            _log.Warning($"Gagal untuk {id}: 0x{lastError:X8} {new Win32Exception(lastError).Message}");
            if (lastError is not (ErrorNoSuchDevinst or ErrorNoDriverSelected))
            {
                break; // galat lain: tidak perlu mencoba Hardware ID berikutnya
            }
        }

        return new DriverOperationResult(false, lastError is ErrorNoSuchDevinst or ErrorNoDriverSelected
            ? $"File driver ini tidak cocok dengan {deviceName}."
            : $"Driver gagal dipasang: {new Win32Exception(lastError).Message} (0x{lastError:X8}).");
    }

    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateDriverForPlugAndPlayDevicesW(
        IntPtr hwndParent, string hardwareId, string fullInfPath, uint installFlags,
        [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);
}
