using System.Management;
using Microsoft.Win32;
using MyDriver.Core.Devices;
using MyDriver.Core.Logging;

namespace MyDriver.Windows.Wmi;

/// <summary>
/// Membaca data perangkat, driver, dan komputer lewat WMI.
/// Hanya membaca; tidak mengubah apa pun. Bisa dipakai tanpa hak Administrator.
/// </summary>
public sealed class WmiService
{
    private readonly ILogService _log;

    public WmiService(ILogService log)
    {
        _log = log;
    }

    public IReadOnlyList<PnpEntityRecord> GetPnpEntities()
    {
        const string wql =
            "SELECT PNPDeviceID, Name, PNPClass, ClassGuid, Manufacturer, Service, ConfigManagerErrorCode, Present, HardwareID, CompatibleID FROM Win32_PnPEntity";

        return Query(wql, o => new PnpEntityRecord(
            DeviceId: GetString(o, "PNPDeviceID") ?? string.Empty,
            Name: GetString(o, "Name"),
            PnpClass: GetString(o, "PNPClass"),
            ClassGuid: GetString(o, "ClassGuid"),
            Manufacturer: GetString(o, "Manufacturer"),
            Service: GetString(o, "Service"),
            ErrorCode: GetInt(o, "ConfigManagerErrorCode"),
            Present: GetBool(o, "Present"),
            HardwareIds: GetStringArray(o, "HardwareID"),
            CompatibleIds: GetStringArray(o, "CompatibleID")));
    }

    public IReadOnlyList<SignedDriverRecord> GetSignedDrivers()
    {
        const string wql =
            "SELECT DeviceID, DeviceName, DeviceClass, DriverVersion, DriverDate, DriverProviderName, InfName, IsSigned, Signer FROM Win32_PnPSignedDriver";

        return Query(wql, o => new SignedDriverRecord(
            DeviceId: GetString(o, "DeviceID") ?? string.Empty,
            DeviceName: GetString(o, "DeviceName"),
            DeviceClass: GetString(o, "DeviceClass"),
            DriverVersion: GetString(o, "DriverVersion"),
            DriverDate: GetString(o, "DriverDate"),
            DriverProvider: GetString(o, "DriverProviderName"),
            InfName: GetString(o, "InfName"),
            IsSigned: GetBool(o, "IsSigned"),
            Signer: GetString(o, "Signer")));
    }

    public ComputerInfo GetComputerInfo()
    {
        var system = QueryFirst("SELECT Name, Manufacturer, Model, SystemFamily FROM Win32_ComputerSystem");
        var bios = QueryFirst("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS");
        var os = QueryFirst("SELECT Caption, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
        var product = QueryFirst("SELECT UUID FROM Win32_ComputerSystemProduct");

        string? displayVersion = null;
        string? build = Get(os, "BuildNumber");
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            displayVersion = key?.GetValue("DisplayVersion") as string;
            if (build is not null && key?.GetValue("UBR") is int ubr)
            {
                build = $"{build}.{ubr}";
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _log.Warning("Versi rilis Windows tidak bisa dibaca dari registry.", ex);
        }

        return new ComputerInfo
        {
            ComputerName = Get(system, "Name"),
            Manufacturer = CleanOem(Get(system, "Manufacturer")),
            Model = CleanOem(Get(system, "Model")),
            SystemFamily = CleanOem(Get(system, "SystemFamily")),
            BiosVendor = Get(bios, "Manufacturer"),
            BiosVersion = Get(bios, "SMBIOSBIOSVersion"),
            BiosDate = DeviceInventoryBuilder.ParseCimDate(Get(bios, "ReleaseDate")),
            SerialNumber = CleanOem(Get(bios, "SerialNumber")),
            HardwareUuid = Get(product, "UUID"),
            OsName = Get(os, "Caption")?.Replace("Microsoft ", string.Empty, StringComparison.Ordinal),
            OsDisplayVersion = displayVersion,
            OsBuild = build,
            OsArchitecture = Get(os, "OSArchitecture"),
        };

        static string? Get(Dictionary<string, object?>? row, string name) =>
            row is not null && row.TryGetValue(name, out var v) && v is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    }

    /// <summary>Nilai bawaan pabrik yang tidak berarti (mis. "To be filled by O.E.M.") dianggap kosong.</summary>
    internal static string? CleanOem(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] placeholders =
        [
            "To be filled by O.E.M.", "To Be Filled By O.E.M.", "System manufacturer", "System Product Name",
            "Default string", "Not Applicable", "None", "0", "System Serial Number", "Not Specified",
        ];
        var trimmed = value.Trim();
        return placeholders.Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase)) ? null : trimmed;
    }

    private IReadOnlyList<T> Query<T>(string wql, Func<ManagementBaseObject, T> map)
    {
        var list = new List<T>();
        var options = new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"), new ObjectQuery(wql), options);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                try
                {
                    list.Add(map(item));
                }
                catch (ManagementException ex)
                {
                    _log.Warning($"Satu baris WMI dilewati: {ex.Message}");
                }
            }
        }
        return list;
    }

    private Dictionary<string, object?>? QueryFirst(string wql)
    {
        try
        {
            return Query(wql, o => o.Properties.Cast<PropertyData>().ToDictionary(p => p.Name, p => (object?)p.Value))
                .FirstOrDefault();
        }
        catch (ManagementException ex)
        {
            _log.Warning($"Query WMI gagal: {wql}", ex);
            return null;
        }
    }

    private static object? GetValue(ManagementBaseObject o, string name)
    {
        try
        {
            return o[name];
        }
        catch (ManagementException)
        {
            // Properti tidak ada di versi Windows ini.
            return null;
        }
    }

    private static string? GetString(ManagementBaseObject o, string name) => GetValue(o, name) as string;

    private static int? GetInt(ManagementBaseObject o, string name) => GetValue(o, name) switch
    {
        uint u => (int)u,
        int i => i,
        ushort s => s,
        _ => null,
    };

    private static bool? GetBool(ManagementBaseObject o, string name) => GetValue(o, name) as bool?;

    private static IReadOnlyList<string>? GetStringArray(ManagementBaseObject o, string name) =>
        GetValue(o, name) as string[];
}
