namespace MyDriver.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Pengaturan aplikasi yang disimpan di <c>settings.json</c>.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Jam scan harian otomatis (0–23). Scan saat logon selalu ikut bila pemantauan aktif.</summary>
    public int DailyScanHour { get; set; } = 12;

    /// <summary>Tampilkan notifikasi bila scan otomatis menemukan masalah baru.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Sembunyikan perangkat yang sedang tidak tersambung (Kode 45).</summary>
    public bool HideDisconnectedDevices { get; set; } = true;
}
