namespace MyDriver.Core.Diagnosis;

public enum ActionKind
{
    /// <summary>Dijalankan aplikasi lewat tombol.</summary>
    Button,

    /// <summary>Langkah yang dikerjakan pengguna sendiri; aplikasi hanya memberi petunjuk.</summary>
    Manual,
}

/// <summary>Satu jenis aksi perbaikan yang bisa direkomendasikan aturan.</summary>
/// <param name="NeedsDevice">Aksi ini hanya berlaku untuk satu perangkat tertentu.</param>
public sealed record ActionInfo(string Id, string Label, string Description, ActionKind Kind, bool RequiresAdministrator, bool NeedsDevice = false);

/// <summary>Daftar semua aksi yang boleh dipakai di <c>rules.json</c>.</summary>
public static class ActionCatalog
{
    public const string RestartDevice = "restartDevice";
    public const string DisableEnableDevice = "disableEnableDevice";
    public const string EnableDevice = "enableDevice";
    public const string RescanHardware = "rescanHardware";
    public const string RollbackToSnapshot = "rollbackToSnapshot";
    public const string InstallFromBackup = "installFromBackup";
    public const string UninstallAndRescan = "uninstallAndRescan";
    public const string WindowsUpdate = "windowsUpdate";
    public const string RestartWindows = "restartWindows";
    public const string ReenableDriverService = "reenableDriverService";
    public const string SystemRestore = "systemRestore";
    public const string BackupNow = "backupNow";
    public const string CopyHardwareId = "copyHardwareId";
    public const string CheckConnection = "checkConnection";
    public const string CheckHardware = "checkHardware";
    public const string CheckBios = "checkBios";
    public const string DisableUnusedDevices = "disableUnusedDevices";
    public const string ManufacturerSite = "manufacturerSite";
    public const string CheckPower = "checkPower";
    public const string MarkHealthy = "markHealthy";

    private static readonly Dictionary<string, ActionInfo> Actions = new ActionInfo[]
    {
        new(RestartDevice, "Restart perangkat", "Mematikan lalu menyalakan ulang perangkat ini saja, tanpa restart Windows.", ActionKind.Button, true, NeedsDevice: true),
        new(DisableEnableDevice, "Nonaktifkan lalu aktifkan", "Mematikan perangkat sebentar lalu mengaktifkannya lagi.", ActionKind.Button, true, NeedsDevice: true),
        new(EnableDevice, "Aktifkan perangkat", "Menyalakan kembali perangkat yang dinonaktifkan.", ActionKind.Button, true, NeedsDevice: true),
        new(RescanHardware, "Scan ulang hardware", "Meminta Windows mendeteksi ulang semua perangkat yang tersambung.", ActionKind.Button, true),
        new(RollbackToSnapshot, "Kembalikan ke versi snapshot", "Memasang lagi versi driver yang tercatat saat kondisi sehat.", ActionKind.Button, true, NeedsDevice: true),
        new(InstallFromBackup, "Pasang dari backup", "Memasang driver perangkat ini dari backup yang tersimpan.", ActionKind.Button, true, NeedsDevice: true),
        new(UninstallAndRescan, "Uninstall driver lalu scan ulang", "Menghapus perangkat beserta drivernya, lalu membiarkan Windows memasangnya ulang.", ActionKind.Button, true, NeedsDevice: true),
        new(WindowsUpdate, "Cari di Windows Update", "Mencari driver resmi yang lebih baru lewat Windows Update.", ActionKind.Button, true, NeedsDevice: true),
        new(RestartWindows, "Restart Windows", "Menyalakan ulang komputer. Simpan pekerjaan Anda lebih dulu.", ActionKind.Button, false),
        new(ReenableDriverService, "Aktifkan ulang layanan driver", "Menyalakan kembali layanan Windows yang menjalankan driver ini.", ActionKind.Button, true, NeedsDevice: true),
        new(SystemRestore, "Kembali ke restore point sebelumnya", "Membuka System Restore untuk memilih titik pemulihan sebelum masalah muncul.", ActionKind.Button, false),
        new(BackupNow, "Backup sekarang", "Menyimpan salinan semua driver pihak ketiga ke folder data.", ActionKind.Button, true),
        new(CopyHardwareId, "Salin Hardware ID", "Menyalin kode pengenal perangkat untuk dicari di situs resmi produsen.", ActionKind.Button, false, NeedsDevice: true),
        new(CheckConnection, "Cek sambungan fisik", "Cabut lalu pasang lagi perangkatnya, coba port lain, atau periksa kabelnya.", ActionKind.Manual, false),
        new(CheckHardware, "Periksakan hardware ke teknisi", "Bila langkah lain tidak berhasil, perangkatnya sendiri mungkin rusak.", ActionKind.Manual, false),
        new(CheckBios, "Cek pengaturan BIOS", "Pastikan perangkat ini aktif di pengaturan BIOS/UEFI dan BIOS sudah versi terbaru.", ActionKind.Manual, false),
        new(DisableUnusedDevices, "Nonaktifkan perangkat yang tidak dipakai", "Mematikan perangkat yang tidak dipakai bisa membebaskan sumber daya yang diperebutkan.", ActionKind.Manual, false),
        new(ManufacturerSite, "Cari driver di situs resmi produsen", "Gunakan merek, model, dan Hardware ID untuk mencari driver di situs resmi.", ActionKind.Manual, false),
        new(MarkHealthy, "Tandai sebagai sehat", "Mencatat driver yang terpasang sekarang sebagai acuan kondisi sehat untuk perangkat ini.", ActionKind.Button, false, NeedsDevice: true),
        new(CheckPower, "Cek baterai, charger, dan suhu", "Mati mendadak sering disebabkan baterai lemah, charger longgar, atau laptop terlalu panas.", ActionKind.Manual, false),
    }.ToDictionary(a => a.Id, StringComparer.Ordinal);

    public static IReadOnlyCollection<ActionInfo> All => Actions.Values;

    public static bool IsKnown(string id) => Actions.ContainsKey(id);

    public static ActionInfo Get(string id) => Actions[id];
}
