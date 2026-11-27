namespace MyDriver.Core.Devices;

/// <summary>Keadaan dasar perangkat, diambil langsung dari kode error Windows.</summary>
public enum DeviceStatus
{
    Normal,
    Disabled,
    Problem,
    NotConnected,
}

/// <summary>Satu perangkat PnP beserta driver yang dipakainya.</summary>
public sealed record DeviceInfo
{
    /// <summary>Kode error ConfigManager untuk perangkat yang sedang tidak tersambung.</summary>
    public const int ErrorNotConnected = 45;

    /// <summary>Kode error ConfigManager untuk perangkat yang dinonaktifkan.</summary>
    public const int ErrorDisabled = 22;

    /// <summary>ID instans perangkat (PNPDeviceID), unik per perangkat.</summary>
    public required string InstanceId { get; init; }

    public required string Name { get; init; }

    /// <summary>Kelas PnP Windows, mis. "Net", "MEDIA", "Display".</summary>
    public string? PnpClass { get; init; }

    public string? ClassGuid { get; init; }

    public DeviceCategory Category { get; init; }

    public string? Manufacturer { get; init; }

    /// <summary>Nama layanan driver (mis. "Netwtw14").</summary>
    public string? Service { get; init; }

    /// <summary>ConfigManagerErrorCode; 0 = berfungsi normal.</summary>
    public int ErrorCode { get; init; }

    public bool IsPresent { get; init; } = true;

    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    public IReadOnlyList<string> CompatibleIds { get; init; } = [];

    public string? DriverVersion { get; init; }

    public DateTime? DriverDate { get; init; }

    public string? DriverProvider { get; init; }

    /// <summary>Nama file INF di DriverStore (mis. "oem42.inf").</summary>
    public string? InfName { get; init; }

    public bool? IsSigned { get; init; }

    public string? Signer { get; init; }

    public bool HasDriver => DriverVersion is not null || InfName is not null;

    public DeviceStatus Status =>
        !IsPresent || ErrorCode == ErrorNotConnected ? DeviceStatus.NotConnected
        : ErrorCode == 0 ? DeviceStatus.Normal
        : ErrorCode == ErrorDisabled ? DeviceStatus.Disabled
        : DeviceStatus.Problem;
}
