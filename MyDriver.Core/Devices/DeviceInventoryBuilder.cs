using System.Globalization;

namespace MyDriver.Core.Devices;

/// <summary>Data mentah satu baris <c>Win32_PnPEntity</c>.</summary>
public sealed record PnpEntityRecord(
    string DeviceId,
    string? Name,
    string? PnpClass,
    string? ClassGuid,
    string? Manufacturer,
    string? Service,
    int? ErrorCode,
    bool? Present,
    IReadOnlyList<string>? HardwareIds,
    IReadOnlyList<string>? CompatibleIds);

/// <summary>Data mentah satu baris <c>Win32_PnPSignedDriver</c>.</summary>
public sealed record SignedDriverRecord(
    string DeviceId,
    string? DeviceName,
    string? DeviceClass,
    string? DriverVersion,
    string? DriverDate,
    string? DriverProvider,
    string? InfName,
    bool? IsSigned,
    string? Signer);

/// <summary>
/// Menggabungkan data perangkat (<c>Win32_PnPEntity</c>) dengan data driver
/// (<c>Win32_PnPSignedDriver</c>) berdasarkan ID perangkat. Logika murni agar mudah diuji.
/// </summary>
public static class DeviceInventoryBuilder
{
    public static IReadOnlyList<DeviceInfo> Build(
        IEnumerable<PnpEntityRecord> entities,
        IEnumerable<SignedDriverRecord> drivers)
    {
        var driverById = new Dictionary<string, SignedDriverRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in drivers)
        {
            if (!string.IsNullOrWhiteSpace(driver.DeviceId))
            {
                driverById.TryAdd(driver.DeviceId, driver);
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DeviceInfo>();
        foreach (var entity in entities)
        {
            if (string.IsNullOrWhiteSpace(entity.DeviceId) || !seen.Add(entity.DeviceId))
            {
                continue;
            }

            driverById.TryGetValue(entity.DeviceId, out var driver);
            var pnpClass = Clean(entity.PnpClass) ?? Clean(driver?.DeviceClass);

            result.Add(new DeviceInfo
            {
                InstanceId = entity.DeviceId,
                Name = Clean(entity.Name) ?? Clean(driver?.DeviceName) ?? entity.DeviceId,
                PnpClass = pnpClass,
                ClassGuid = Clean(entity.ClassGuid),
                Category = DeviceCategories.FromPnpClass(pnpClass),
                Manufacturer = Clean(entity.Manufacturer),
                Service = Clean(entity.Service),
                ErrorCode = entity.ErrorCode ?? 0,
                IsPresent = entity.Present ?? true,
                HardwareIds = CleanList(entity.HardwareIds),
                CompatibleIds = CleanList(entity.CompatibleIds),
                DriverVersion = Clean(driver?.DriverVersion),
                DriverDate = ParseCimDate(driver?.DriverDate),
                DriverProvider = Clean(driver?.DriverProvider),
                InfName = Clean(driver?.InfName),
                IsSigned = driver?.IsSigned,
                Signer = Clean(driver?.Signer),
            });
        }

        return result
            .OrderBy(d => d.Category)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Melengkapi perangkat dengan status tanda tangan dari data <c>Win32_PnPSignedDriver</c>.
    /// Data driver lain hanya diisi bila masih kosong: data cfgmgr32 lebih dipercaya, karena
    /// <c>DriverDate</c> dari WMI menukar hari dan bulan pada format tanggal non-AS.
    /// </summary>
    public static IReadOnlyList<DeviceInfo> ApplySignatures(
        IReadOnlyList<DeviceInfo> devices,
        IEnumerable<SignedDriverRecord> drivers)
    {
        var byId = new Dictionary<string, SignedDriverRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in drivers)
        {
            if (!string.IsNullOrWhiteSpace(driver.DeviceId))
            {
                byId.TryAdd(driver.DeviceId, driver);
            }
        }

        return devices
            .Select(d => byId.TryGetValue(d.InstanceId, out var drv)
                ? d with
                {
                    IsSigned = drv.IsSigned ?? d.IsSigned,
                    Signer = Clean(drv.Signer) ?? d.Signer,
                    DriverVersion = d.DriverVersion ?? Clean(drv.DriverVersion),
                    DriverDate = d.DriverDate ?? ParseCimDate(drv.DriverDate),
                    DriverProvider = d.DriverProvider ?? Clean(drv.DriverProvider),
                    InfName = d.InfName ?? Clean(drv.InfName),
                }
                : d)
            .ToList();
    }

    /// <summary>
    /// Mengurai tanggal format CIM (mis. "20230612000000.******+000") menjadi tanggal saja.
    /// Mengembalikan null bila kosong atau tidak valid.
    /// </summary>
    public static DateTime? ParseCimDate(string? value)
    {
        if (value is null || value.Length < 8)
        {
            return null;
        }

        return DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string>? values) =>
        values is null ? [] : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
}
