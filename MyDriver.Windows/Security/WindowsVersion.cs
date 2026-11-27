namespace MyDriver.Windows.Security;

/// <summary>
/// Versi Windows yang sedang berjalan. Berkat <c>supportedOS</c> di app.manifest,
/// <see cref="Environment.OSVersion"/> melaporkan nomor build yang sebenarnya.
/// </summary>
public static class WindowsVersion
{
    /// <summary>Windows 10 versi 2004: pnputil mulai punya /restart-device, /remove-device, /scan-devices.</summary>
    public const int Windows10_2004 = 19041;

    /// <summary>Windows 10 versi 22H2, target utama PRD.</summary>
    public const int Windows10_22H2 = 19045;

    public const int Windows11 = 22000;

    public static int Build => Environment.OSVersion.Version.Build;

    public static bool IsWindows11 => Build >= Windows11;

    /// <summary>Perintah perangkat pnputil (restart/disable/enable/remove/scan) tersedia.</summary>
    public static bool SupportsPnpUtilDeviceCommands => Build >= Windows10_2004;

    /// <summary>Di bawah target PRD (Windows 10 22H2): aplikasi tetap jalan, dengan peringatan.</summary>
    public static bool IsOlderThanSupported => Build < Windows10_22H2;

    public static bool Is64BitWindows => Environment.Is64BitOperatingSystem;
}
