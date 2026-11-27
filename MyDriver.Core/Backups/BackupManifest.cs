namespace MyDriver.Core.Backups;

/// <summary>Perangkat yang memakai sebuah paket driver saat backup dibuat.</summary>
public sealed record BackupDevice(string InstanceId, string Name, IReadOnlyList<string> HardwareIds);

/// <summary>Satu paket driver hasil <c>dism /export-driver</c> (satu subfolder).</summary>
public sealed record BackupPackage
{
    /// <summary>Nama subfolder, mis. "netrtwlane.inf_amd64_1a2b3c4d".</summary>
    public required string FolderName { get; init; }

    /// <summary>Path file INF relatif terhadap folder backup.</summary>
    public string? InfFile { get; init; }

    /// <summary>Nama terbit di DriverStore saat backup, mis. "oem12.inf".</summary>
    public string? PublishedName { get; init; }

    public string? Provider { get; init; }
    public string? Version { get; init; }
    public DateTime? Date { get; init; }
    public string? PnpClass { get; init; }
    public IReadOnlyList<BackupDevice> Devices { get; init; } = [];

    /// <summary>Nama yang mudah dibaca: nama perangkat pertama, atau nama INF asli.</summary>
    public string DisplayName =>
        Devices.Count > 0 ? Devices[0].Name : OriginalInfName;

    /// <summary>"netrtwlane.inf_amd64_1a2b" → "netrtwlane.inf".</summary>
    public string OriginalInfName
    {
        get
        {
            var index = FolderName.IndexOf(".inf_", StringComparison.OrdinalIgnoreCase);
            return index > 0 ? FolderName[..(index + 4)] : FolderName;
        }
    }
}

/// <summary>Isi <c>backup-manifest.json</c> di setiap folder backup.</summary>
public sealed record BackupManifest
{
    public const string FileName = "backup-manifest.json";

    public int FormatVersion { get; init; } = 1;
    public required DateTime CreatedAt { get; init; }
    public required string ComputerId { get; init; }
    public string? ComputerFolder { get; init; }
    public string? Windows { get; init; }
    public long TotalBytes { get; init; }
    public IReadOnlyList<BackupPackage> Packages { get; init; } = [];
}

/// <summary>Satu folder backup beserta manifest-nya (null bila backup tidak selesai).</summary>
public sealed record BackupInfo(string Directory, string Name, DateTime CreatedAt, BackupManifest? Manifest, long SizeBytes)
{
    public bool IsComplete => Manifest is not null;

    public int PackageCount => Manifest?.Packages.Count ?? 0;
}
