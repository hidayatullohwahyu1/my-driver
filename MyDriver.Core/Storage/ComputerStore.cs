using System.Globalization;
using System.Text.Json;
using MyDriver.Core.Backups;
using MyDriver.Core.Devices;
using MyDriver.Core.Logging;
using MyDriver.Core.Snapshots;

namespace MyDriver.Core.Storage;

/// <summary>Isi <c>computer.json</c>.</summary>
public sealed class ComputerProfile
{
    public required string Id { get; set; }
    public required string FolderName { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Bios { get; set; }
    public string? Windows { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    /// <summary>True setelah alur "pertama kali dijalankan" selesai atau dilewati.</summary>
    public bool OnboardingCompleted { get; set; }
}

/// <summary>
/// Folder data satu komputer: <c>MyDriverData/computers/MEREK_MODEL_ID/</c>.
/// Berisi computer.json, snapshot-healthy.json, scans/, history.json, backups/.
/// </summary>
public sealed class ComputerStore
{
    public const int MaxSavedScans = 100;

    private readonly ILogService _log;

    private ComputerStore(string directory, ComputerProfile profile, ILogService log)
    {
        Directory = directory;
        Profile = profile;
        _log = log;
    }

    public string Directory { get; }
    public ComputerProfile Profile { get; }

    public string ProfileFile => Path.Combine(Directory, "computer.json");
    public string SnapshotFile => Path.Combine(Directory, "snapshot-healthy.json");
    public string ScansDirectory => Path.Combine(Directory, "scans");
    public string HistoryFile => Path.Combine(Directory, "history.json");
    public string BackupsDirectory => Path.Combine(Directory, "backups");

    /// <summary>True bila folder komputer ini baru saja dibuat (komputer belum pernah dipindai).</summary>
    public bool IsNew { get; private set; }

    /// <summary>
    /// Membuka (atau membuat) folder untuk komputer ini. Folder lama dengan ID yang sama
    /// dipakai lagi walau nama model berubah.
    /// </summary>
    public static ComputerStore Open(DataPaths paths, ComputerInfo computer, DateTime now, ILogService log)
    {
        var id = ComputerIdentity.ComputeId(computer.HardwareUuid, computer.SerialNumber, computer.ComputerName);
        System.IO.Directory.CreateDirectory(paths.ComputersDirectory);

        var existing = System.IO.Directory
            .EnumerateDirectories(paths.ComputersDirectory, "*_" + id)
            .FirstOrDefault();
        var directory = existing
            ?? Path.Combine(paths.ComputersDirectory, ComputerIdentity.FolderName(computer.Manufacturer, computer.Model, id));

        ComputerProfile? profile = null;
        var profileFile = Path.Combine(directory, "computer.json");
        try
        {
            profile = JsonFiles.Read<ComputerProfile>(profileFile);
        }
        catch (JsonException ex)
        {
            log.Warning($"computer.json rusak, dibuat ulang: {profileFile}", ex);
        }

        var isNew = profile is null;
        profile ??= new ComputerProfile { Id = id, FolderName = Path.GetFileName(directory), FirstSeen = now };
        profile.Manufacturer = computer.Manufacturer;
        profile.Model = computer.Model;
        profile.SerialNumber = computer.SerialNumber;
        profile.Bios = computer.BiosText;
        profile.Windows = computer.WindowsText;
        profile.LastSeen = now;

        var store = new ComputerStore(directory, profile, log) { IsNew = isNew };
        store.SaveProfile();
        log.Info($"Folder komputer: {directory}{(isNew ? " (baru)" : string.Empty)}");
        return store;
    }

    public void SaveProfile() => JsonFiles.Write(ProfileFile, Profile);

    public HealthySnapshot? LoadSnapshot()
    {
        try
        {
            return JsonFiles.Read<HealthySnapshot>(SnapshotFile);
        }
        catch (JsonException ex)
        {
            _log.Warning($"snapshot-healthy.json rusak dan diabaikan: {SnapshotFile}", ex);
            return null;
        }
    }

    public void SaveSnapshot(HealthySnapshot snapshot)
    {
        JsonFiles.Write(SnapshotFile, snapshot);
        _log.Info($"Snapshot sehat disimpan: {snapshot.Drivers.Count} driver.");
    }

    /// <summary>Menyimpan scan ke <c>scans/yyyy-MM-dd_HHmm.json</c> dan membuang yang paling lama bila lebih dari 100.</summary>
    public string SaveScan(ScanResult scan)
    {
        var file = Path.Combine(ScansDirectory, $"{scan.ScannedAt:yyyy-MM-dd_HHmm}.json");
        JsonFiles.Write(file, scan);

        foreach (var old in System.IO.Directory.GetFiles(ScansDirectory, "*.json")
                     .OrderByDescending(f => f, StringComparer.Ordinal)
                     .Skip(MaxSavedScans))
        {
            File.Delete(old);
        }
        return file;
    }

    /// <summary>Membuat folder backup baru <c>backups/yyyy-MM-dd</c> (diberi akhiran jam bila sudah ada).</summary>
    public string CreateBackupDirectory(DateTime now)
    {
        var name = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = Path.Combine(BackupsDirectory, name);
        if (System.IO.Directory.Exists(path))
        {
            path = Path.Combine(BackupsDirectory, now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
        }
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    public void SaveBackupManifest(string backupDirectory, BackupManifest manifest) =>
        JsonFiles.Write(Path.Combine(backupDirectory, BackupManifest.FileName), manifest);

    /// <summary>Semua backup komputer ini, terbaru lebih dulu.</summary>
    public IReadOnlyList<BackupInfo> ListBackups()
    {
        if (!System.IO.Directory.Exists(BackupsDirectory))
        {
            return [];
        }

        var list = new List<BackupInfo>();
        foreach (var dir in System.IO.Directory.EnumerateDirectories(BackupsDirectory))
        {
            BackupManifest? manifest = null;
            try
            {
                manifest = JsonFiles.Read<BackupManifest>(Path.Combine(dir, BackupManifest.FileName));
            }
            catch (JsonException ex)
            {
                _log.Warning($"Manifest backup rusak: {dir}", ex);
            }

            var created = manifest?.CreatedAt ?? System.IO.Directory.GetCreationTime(dir);
            var size = manifest?.TotalBytes ?? DirectorySize(dir);
            list.Add(new BackupInfo(dir, Path.GetFileName(dir), created, manifest, size));
        }
        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <summary>Menghapus satu folder backup. Hanya folder di dalam backups/ komputer ini yang boleh dihapus.</summary>
    public void DeleteBackup(BackupInfo backup)
    {
        var full = Path.GetFullPath(backup.Directory);
        var root = Path.GetFullPath(BackupsDirectory) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Folder di luar folder backup komputer ini: {full}");
        }
        System.IO.Directory.Delete(full, recursive: true);
        _log.Info($"Backup dihapus: {full}");
    }

    public static long DirectorySize(string directory)
    {
        try
        {
            return new DirectoryInfo(directory)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
