using MyDriver.Core.Devices;

namespace MyDriver.Core.Backups;

/// <summary>Folder hasil ekspor DISM beserta file INF di dalamnya.</summary>
public sealed record ExportedPackage(string FolderName, string? InfFile);

/// <summary>
/// Menyusun isi manifest backup: mencocokkan setiap paket hasil ekspor dengan perangkat
/// yang memakainya, lewat nama folder DriverStore dari INF terbit (oemNN.inf).
/// </summary>
public static class BackupManifestBuilder
{
    /// <param name="exported">Subfolder yang ada di folder backup.</param>
    /// <param name="scan">Scan terbaru, untuk tahu perangkat mana memakai paket mana.</param>
    /// <param name="driverStoreFolderOf">
    /// Mengubah nama INF terbit ("oem12.inf") menjadi nama folder DriverStore-nya
    /// ("netrtwlane.inf_amd64_1a2b"); null bila tidak ditemukan.
    /// </param>
    public static IReadOnlyList<BackupPackage> BuildPackages(
        IEnumerable<ExportedPackage> exported,
        ScanResult scan,
        Func<string, string?> driverStoreFolderOf)
    {
        var devicesByFolder = new Dictionary<string, List<DeviceInfo>>(StringComparer.OrdinalIgnoreCase);
        var publishedByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in scan.Devices
                     .Where(d => d.InfName?.StartsWith("oem", StringComparison.OrdinalIgnoreCase) == true)
                     .GroupBy(d => d.InfName!, StringComparer.OrdinalIgnoreCase))
        {
            var folder = driverStoreFolderOf(group.Key);
            if (folder is null)
            {
                continue;
            }
            publishedByFolder[folder] = group.Key;
            devicesByFolder[folder] = group.ToList();
        }

        return exported
            .Select(e =>
            {
                devicesByFolder.TryGetValue(e.FolderName, out var devices);
                publishedByFolder.TryGetValue(e.FolderName, out var published);
                var first = devices?.FirstOrDefault();
                return new BackupPackage
                {
                    FolderName = e.FolderName,
                    InfFile = e.InfFile,
                    PublishedName = published,
                    Provider = first?.DriverProvider,
                    Version = first?.DriverVersion,
                    Date = first?.DriverDate,
                    PnpClass = first?.PnpClass,
                    Devices = devices?
                        .Select(d => new BackupDevice(d.InstanceId, d.Name, d.HardwareIds))
                        .ToList() ?? [],
                };
            })
            .OrderBy(p => p.Devices.Count == 0)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Paket backup yang dulu dipakai perangkat ini (dicocokkan lewat ID instans, lalu Hardware ID).</summary>
    public static BackupPackage? FindPackageFor(BackupManifest manifest, DeviceInfo device)
    {
        var byInstance = manifest.Packages.FirstOrDefault(p =>
            p.Devices.Any(d => string.Equals(d.InstanceId, device.InstanceId, StringComparison.OrdinalIgnoreCase)));
        if (byInstance is not null)
        {
            return byInstance;
        }

        var ids = new HashSet<string>(device.HardwareIds, StringComparer.OrdinalIgnoreCase);
        return ids.Count == 0
            ? null
            : manifest.Packages.FirstOrDefault(p => p.Devices.Any(d => d.HardwareIds.Any(ids.Contains)));
    }
}

public static class BackupRetention
{
    public const int KeepCount = 5;

    /// <summary>Backup di luar 5 terbaru, untuk ditawarkan dihapus.</summary>
    public static IReadOnlyList<BackupInfo> OldBackups(IEnumerable<BackupInfo> backups, int keep = KeepCount) =>
        backups.OrderByDescending(b => b.CreatedAt).Skip(keep).ToList();
}
