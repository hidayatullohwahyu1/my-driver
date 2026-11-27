using MyDriver.Core.Devices;
using MyDriver.Windows.Wmi;

namespace MyDriver.Tests;

/// <summary>
/// Membaca WMI sungguhan di komputer yang menjalankan test (hanya membaca, tanpa admin).
/// Memastikan scan lengkap selesai &lt; 15 detik sesuai kriteria PRD.
/// </summary>
[Trait("Category", "Integration")]
public class WmiScanIntegrationTests
{
    [Fact]
    public async Task ScanAsync_ReadsRealDevicesWithinFifteenSeconds()
    {
        var log = new MemoryLog();
        var scanner = new WmiDeviceScanner(new WmiService(log), log);

        var result = await scanner.ScanAsync();

        Assert.True(result.Duration < TimeSpan.FromSeconds(15), $"Scan terlalu lama: {result.Duration}");
        Assert.True(result.ConnectedCount > 10, $"Perangkat terlalu sedikit: {result.ConnectedCount}");
        Assert.Contains(result.Devices, d => d.HasDriver);
        Assert.Contains(result.Devices, d => d.Category == DeviceCategory.Storage);
        Assert.False(string.IsNullOrWhiteSpace(result.Computer.OsName));
        Assert.Contains(log.Entries, e => e.Message.StartsWith("Scan selesai"));
        Assert.False(result.SignaturesChecked);

        var withSignatures = await scanner.AddSignaturesAsync(result);

        Assert.True(withSignatures.SignaturesChecked);
        Assert.Equal(result.Devices.Count, withSignatures.Devices.Count);
        Assert.Contains(withSignatures.Devices, d => d.IsSigned == true && d.Signer is not null);
    }

    /// <summary>Data driver dari cfgmgr32 harus sama dengan yang dilaporkan WMI.</summary>
    [Fact]
    public async Task FastDriverData_MatchesWmiSignedDriver()
    {
        var log = new MemoryLog();
        var scanner = new WmiDeviceScanner(new WmiService(log), log);
        var fast = await scanner.ScanAsync();
        var wmi = new WmiService(log).GetSignedDrivers()
            .Where(d => d.DriverVersion is not null)
            .ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);

        var compared = 0;
        foreach (var device in fast.Devices.Where(d => d.IsPresent && wmi.ContainsKey(d.InstanceId)))
        {
            var expected = wmi[device.InstanceId];
            Assert.Equal(expected.DriverVersion, device.DriverVersion);
            Assert.Equal(expected.InfName, device.InfName, ignoreCase: true);
            // Tanggal TIDAK dibandingkan: Win32_PnPSignedDriver.DriverDate menukar hari dan bulan
            // pada Windows berformat tanggal non-AS (mis. id-ID). Nilai cfgmgr32 sama dengan registry.
            compared++;
        }
        Assert.True(compared > 10, $"Terlalu sedikit perangkat dibandingkan: {compared}");
    }
}
