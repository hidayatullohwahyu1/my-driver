using MyDriver.Core.Devices;
using MyDriver.Core.Logging;
using MyDriver.Core.Repair;
using MyDriver.Core.Storage;
using MyDriver.Windows.Commands;

namespace MyDriver.Windows.Drivers;

/// <summary>Satu paket driver pihak ketiga di DriverStore.</summary>
public sealed record DriverStorePackage
{
    /// <summary>Nama terbit, mis. "oem12.inf".</summary>
    public required string PublishedName { get; init; }

    /// <summary>Nama INF asli, mis. "netathr.inf".</summary>
    public required string OriginalName { get; init; }

    public string? Provider { get; init; }
    public string? Class { get; init; }
    public string? Version { get; init; }
    public long SizeBytes { get; init; }
    public string? Folder { get; init; }

    /// <summary>Dipakai perangkat yang tersambung saat scan.</summary>
    public bool InUse { get; init; }

    /// <summary>Ada paket lain dengan INF asli yang sama yang sedang dipakai (ini versi lama).</summary>
    public bool IsOlderVersion { get; init; }

    /// <summary>Kelas perangkat sistem penting: tidak ditawarkan untuk dihapus.</summary>
    public bool IsProtectedClass { get; init; }

    public bool CanOfferForRemoval => !InUse && !IsProtectedClass;
}

/// <summary>
/// Pembersihan DriverStore (F10): mendaftar paket driver pihak ketiga, menandai yang tidak dipakai,
/// dan menghapus paket terpilih dengan <c>pnputil /delete-driver</c> TANPA /force
/// (Windows menolak menghapus paket yang masih dipakai).
/// </summary>
public sealed class DriverStoreInventory
{
    private static readonly HashSet<string> ProtectedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "DiskDrive", "SCSIAdapter", "HDC", "System", "Processor", "Computer",
    };

    private readonly ICommandRunner _runner;
    private readonly ILogService _log;

    public DriverStoreInventory(ICommandRunner runner, ILogService log)
    {
        _runner = runner;
        _log = log;
    }

    public Task<IReadOnlyList<DriverStorePackage>> ListAsync(IEnumerable<DeviceInfo> devices) =>
        Task.Run(() => List(devices.ToList()));

    private IReadOnlyList<DriverStorePackage> List(IReadOnlyList<DeviceInfo> devices)
    {
        var inUse = new HashSet<string>(
            devices.Select(d => d.InfName).OfType<string>(), StringComparer.OrdinalIgnoreCase);

        var infDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
        var raw = new List<DriverStorePackage>();
        foreach (var file in Directory.EnumerateFiles(infDirectory, "oem*.inf"))
        {
            var published = Path.GetFileName(file);
            var storeInf = DriverStoreLocator.GetInfPath(published);
            string text;
            try
            {
                text = File.ReadAllText(storeInf ?? file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var cls = InfReader.ParseVersionField(text, "Class");
            raw.Add(new DriverStorePackage
            {
                PublishedName = published,
                OriginalName = storeInf is null ? published : Path.GetFileName(storeInf),
                Provider = InfReader.ParseVersionField(text, "Provider"),
                Class = cls,
                Version = InfReader.ParseDriverVersion(text),
                Folder = storeInf is null ? null : Path.GetDirectoryName(storeInf),
                SizeBytes = storeInf is null ? 0 : ComputerStore.DirectorySize(Path.GetDirectoryName(storeInf)!),
                InUse = inUse.Contains(published),
                IsProtectedClass = cls is not null && ProtectedClasses.Contains(cls),
            });
        }

        var usedOriginals = new HashSet<string>(raw.Where(p => p.InUse).Select(p => p.OriginalName), StringComparer.OrdinalIgnoreCase);
        var list = raw
            .Select(p => p with { IsOlderVersion = !p.InUse && usedOriginals.Contains(p.OriginalName) })
            .OrderByDescending(p => p.CanOfferForRemoval)
            .ThenByDescending(p => p.IsOlderVersion)
            .ThenByDescending(p => p.SizeBytes)
            .ToList();

        _log.Info($"DriverStore: {list.Count} paket pihak ketiga, {list.Count(p => p.CanOfferForRemoval)} tidak dipakai " +
                  $"({list.Where(p => p.CanOfferForRemoval).Sum(p => p.SizeBytes) / 1024 / 1024} MB).");
        return list;
    }

    /// <summary>Menghapus paket satu per satu. Butuh Administrator.</summary>
    public async Task<(int Deleted, IReadOnlyList<string> Failures, IReadOnlyList<string> Commands)> DeleteAsync(IEnumerable<DriverStorePackage> packages)
    {
        var deleted = 0;
        var failures = new List<string>();
        var commands = new List<string>();
        foreach (var package in packages.Where(p => p.CanOfferForRemoval))
        {
            var result = await _runner.RunAsync("pnputil", DeleteArguments(package.PublishedName), TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            commands.Add(result.CommandLine);
            if (result.Succeeded)
            {
                deleted++;
            }
            else
            {
                failures.Add($"{package.PublishedName} ({package.OriginalName}): kode keluar {result.ExitCode}");
            }
        }
        return (deleted, failures, commands);
    }

    /// <summary>Tanpa /force: Windows menolak menghapus paket yang masih dipakai perangkat.</summary>
    internal static IReadOnlyList<string> DeleteArguments(string publishedName) => ["/delete-driver", publishedName];
}
