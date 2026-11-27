namespace MyDriver.Core.Devices;

/// <summary>Hasil satu kali scan perangkat.</summary>
public sealed record ScanResult(
    DateTime ScannedAt,
    TimeSpan Duration,
    ComputerInfo Computer,
    IReadOnlyList<DeviceInfo> Devices,
    bool SignaturesChecked = false)
{
    /// <summary>Perangkat yang sedang terpasang (tidak termasuk yang tidak tersambung).</summary>
    public int ConnectedCount => Devices.Count(d => d.Status != DeviceStatus.NotConnected);

    /// <summary>Perangkat yang melaporkan kode error selain "dinonaktifkan" dan "tidak tersambung".</summary>
    public int ProblemCount => Devices.Count(d => d.Status == DeviceStatus.Problem);

    public int DisabledCount => Devices.Count(d => d.Status == DeviceStatus.Disabled);
}

/// <summary>Membaca semua perangkat dan info komputer.</summary>
public interface IDeviceScanner
{
    /// <summary>Tahap cepat: perangkat, driver, dan info komputer (tanpa info tanda tangan).</summary>
    Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tahap lambat: melengkapi hasil scan dengan status tanda tangan driver.
    /// Bisa makan waktu lebih dari 10 detik, jadi dijalankan di latar.
    /// </summary>
    Task<ScanResult> AddSignaturesAsync(ScanResult result, CancellationToken cancellationToken = default);
}
