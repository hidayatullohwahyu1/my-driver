using System.Diagnostics;
using System.Globalization;
using MyDriver.Core.Devices;
using MyDriver.Core.Logging;
using MyDriver.Windows.Devices;

namespace MyDriver.Windows.Wmi;

/// <summary>
/// Scan dua tahap:
/// 1. Cepat: daftar perangkat (WMI <c>Win32_PnPEntity</c>) + data driver (cfgmgr32) + info komputer.
/// 2. Latar: status tanda tangan driver (WMI <c>Win32_PnPSignedDriver</c>, lambat).
/// </summary>
public sealed class WmiDeviceScanner : IDeviceScanner
{
    private readonly WmiService _wmi;
    private readonly ILogService _log;

    public WmiDeviceScanner(WmiService wmi, ILogService log)
    {
        _wmi = wmi;
        _log = log;
    }

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        _log.Info("Scan perangkat dimulai.");
        var stopwatch = Stopwatch.StartNew();

        var computerTask = Task.Run(_wmi.GetComputerInfo, cancellationToken);
        var devices = await Task.Run(() =>
        {
            var entities = _wmi.GetPnpEntities();
            var drivers = DevicePropertyReader.ReadDrivers(entities.Select(e => e.DeviceId));
            return DeviceInventoryBuilder.Build(entities, drivers);
        }, cancellationToken).ConfigureAwait(false);
        var computer = await computerTask.ConfigureAwait(false);
        stopwatch.Stop();

        var result = new ScanResult(DateTime.Now, stopwatch.Elapsed, computer, devices);
        _log.Info(
            $"Scan selesai dalam {Seconds(stopwatch.Elapsed)} dtk: {devices.Count} perangkat " +
            $"({result.ConnectedCount} tersambung, {result.ProblemCount} bermasalah, {result.DisabledCount} dinonaktifkan), " +
            $"{devices.Count(d => d.HasDriver)} punya driver.");

        foreach (var device in devices.Where(d => d.Status is DeviceStatus.Problem or DeviceStatus.Disabled))
        {
            _log.Info($"  Kode {device.ErrorCode}: {device.Name} [{device.InstanceId}]");
        }

        return result;
    }

    public async Task<ScanResult> AddSignaturesAsync(ScanResult result, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var signed = await Task.Run(_wmi.GetSignedDrivers, cancellationToken).ConfigureAwait(false);
        var devices = DeviceInventoryBuilder.ApplySignatures(result.Devices, signed);
        stopwatch.Stop();

        var unsigned = devices.Where(d => d.IsSigned == false).ToList();
        _log.Info($"Pemeriksaan tanda tangan selesai dalam {Seconds(stopwatch.Elapsed)} dtk: {unsigned.Count} driver tidak bertanda tangan.");
        foreach (var device in unsigned)
        {
            _log.Info($"  Tidak bertanda tangan: {device.Name} ({device.InfName})");
        }

        return result with { Devices = devices, SignaturesChecked = true };
    }

    private static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
}
