using MyDriver.Core.Devices;

namespace MyDriver.Core.Snapshots;

/// <summary>Driver satu perangkat pada saat kondisi ditandai sehat.</summary>
public sealed record SnapshotDriver(
    string InstanceId,
    string Name,
    string? PnpClass,
    DeviceCategory Category,
    IReadOnlyList<string> HardwareIds,
    string? DriverVersion,
    DateTime? DriverDate,
    string? DriverProvider,
    string? InfName);

/// <summary>
/// Acuan driver "sehat" (<c>snapshot-healthy.json</c>): daftar driver yang sedang berfungsi
/// saat pengguna menyatakan semua perangkat normal.
/// </summary>
public sealed record HealthySnapshot(DateTime CreatedAt, IReadOnlyList<SnapshotDriver> Drivers)
{
    /// <summary>
    /// "Tandai sebagai sehat" untuk satu perangkat: entri lamanya (dicocokkan lewat ID instans
    /// atau Hardware ID) diganti dengan driver yang terpasang sekarang.
    /// </summary>
    public HealthySnapshot WithDevice(DeviceInfo device)
    {
        var ids = new HashSet<string>(device.HardwareIds, StringComparer.OrdinalIgnoreCase);
        var kept = Drivers.Where(d =>
            !string.Equals(d.InstanceId, device.InstanceId, StringComparison.OrdinalIgnoreCase)
            && !d.HardwareIds.Any(ids.Contains));
        var updated = new SnapshotDriver(
            device.InstanceId, device.Name, device.PnpClass, device.Category, device.HardwareIds,
            device.DriverVersion, device.DriverDate, device.DriverProvider, device.InfName);
        return this with { Drivers = kept.Append(updated).ToList() };
    }

    /// <summary>Hanya perangkat yang berfungsi normal dan punya driver yang dimasukkan.</summary>
    public static HealthySnapshot FromScan(ScanResult scan, DateTime createdAt) =>
        new(createdAt, scan.Devices
            .Where(d => d.Status == DeviceStatus.Normal && d.HasDriver)
            .Select(d => new SnapshotDriver(
                d.InstanceId, d.Name, d.PnpClass, d.Category, d.HardwareIds,
                d.DriverVersion, d.DriverDate, d.DriverProvider, d.InfName))
            .ToList());
}
