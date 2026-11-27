using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;

namespace MyDriver.Core.Repair;

public enum ActionPermission
{
    Allowed,

    /// <summary>Boleh, tetapi dengan peringatan merah (perangkat terlindung).</summary>
    AllowedWithRedWarning,

    Blocked,
}

public sealed record ActionCheck(ActionPermission Permission, string? Reason = null)
{
    public bool IsAllowed => Permission != ActionPermission.Blocked;
}

/// <summary>
/// Pengaman sistem: perangkat penyimpanan, chipset/ACPI, prosesor, dan perangkat boot tidak boleh
/// di-uninstall, dinonaktifkan, atau di-restart dari aplikasi; hanya "kembalikan ke snapshot"
/// (dan pasang dari backup) dengan peringatan merah.
/// </summary>
public static class ActionPolicy
{
    private static readonly HashSet<string> ProtectedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "DiskDrive", "SCSIAdapter", "HDC", "System", "Processor", "Computer",
    };

    private static readonly HashSet<string> BlockedOnProtected = new(StringComparer.Ordinal)
    {
        ActionCatalog.UninstallAndRescan,
        ActionCatalog.DisableEnableDevice,
        ActionCatalog.RestartDevice,
        ActionCatalog.ReenableDriverService,
    };

    private static readonly HashSet<string> RedWarningOnProtected = new(StringComparer.Ordinal)
    {
        ActionCatalog.RollbackToSnapshot,
        ActionCatalog.InstallFromBackup,
    };

    /// <summary>Aksi yang mengubah driver atau sistem: wajib restore point lebih dulu.</summary>
    private static readonly HashSet<string> NeedsRestorePointActions = new(StringComparer.Ordinal)
    {
        ActionCatalog.RollbackToSnapshot,
        ActionCatalog.InstallFromBackup,
        ActionCatalog.UninstallAndRescan,
        ActionCatalog.ReenableDriverService,
    };

    public static bool IsProtected(DeviceInfo? device) =>
        device?.PnpClass is { } cls && ProtectedClasses.Contains(cls);

    public static bool NeedsRestorePoint(string actionId) => NeedsRestorePointActions.Contains(actionId);

    public static ActionCheck Check(string actionId, DeviceInfo? device)
    {
        if (!IsProtected(device))
        {
            return new ActionCheck(ActionPermission.Allowed);
        }
        if (BlockedOnProtected.Contains(actionId))
        {
            return new ActionCheck(ActionPermission.Blocked,
                $"{device!.Name} termasuk perangkat yang dibutuhkan Windows untuk menyala (penyimpanan, chipset, atau prosesor). " +
                "Demi keamanan, aksi ini tidak bisa dijalankan dari aplikasi.");
        }
        if (RedWarningOnProtected.Contains(actionId))
        {
            return new ActionCheck(ActionPermission.AllowedWithRedWarning,
                $"PERINGATAN: {device!.Name} adalah perangkat sistem penting. Driver yang salah bisa membuat Windows tidak bisa menyala. " +
                "Lanjutkan hanya bila Anda yakin, dan pastikan restore point berhasil dibuat.");
        }
        return new ActionCheck(ActionPermission.Allowed);
    }

    /// <summary>Akibat yang dirasakan pengguna saat perangkat dihentikan sebentar.</summary>
    public static string Impact(DeviceInfo? device) => device?.Category switch
    {
        DeviceCategory.Network => "Koneksi jaringan akan terputus sebentar.",
        DeviceCategory.Audio => "Suara akan mati sebentar.",
        DeviceCategory.Display => "Layar bisa berkedip atau gelap beberapa detik.",
        DeviceCategory.Usb => "Perangkat USB yang tersambung bisa terputus sebentar.",
        DeviceCategory.Bluetooth => "Perangkat Bluetooth akan terputus sebentar.",
        DeviceCategory.Storage => "Akses ke drive bisa terhenti sebentar.",
        null => "Sistem akan diubah.",
        _ => "Perangkat ini akan berhenti bekerja sebentar.",
    };
}
