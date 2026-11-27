namespace MyDriver.Core.Repair;

public enum HistoryKind
{
    Scan,
    Action,
    Backup,
    Restore,
    Resolved,
}

/// <summary>Satu baris <c>history.json</c>: scan, aksi perbaikan, backup, atau pemulihan beserta hasilnya.</summary>
public sealed record HistoryEntry
{
    public required DateTime Time { get; init; }
    public required HistoryKind Kind { get; init; }

    /// <summary>Kalimat singkat, mis. "Restart perangkat: Qualcomm WiFi".</summary>
    public required string Title { get; init; }

    public bool Success { get; init; } = true;

    /// <summary>Pesan hasil yang ditampilkan ke pengguna.</summary>
    public string? Message { get; init; }

    public string? ActionId { get; init; }
    public string? DeviceName { get; init; }
    public string? InstanceId { get; init; }

    /// <summary>Keterangan restore point yang dibuat atau dipakai sebelum aksi.</summary>
    public string? RestorePoint { get; init; }

    /// <summary>Perintah yang dijalankan (untuk detail teknis).</summary>
    public IReadOnlyList<string> Commands { get; init; } = [];
}
