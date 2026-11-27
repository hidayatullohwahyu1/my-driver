namespace MyDriver.Core.Devices;

/// <summary>Kategori tampilan perangkat (urutan enum = urutan di layar).</summary>
public enum DeviceCategory
{
    Network,
    Audio,
    Display,
    Usb,
    Bluetooth,
    Storage,
    Other,
}

public static class DeviceCategories
{
    private static readonly Dictionary<string, DeviceCategory> ByPnpClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Net"] = DeviceCategory.Network,

        ["MEDIA"] = DeviceCategory.Audio,
        ["AudioEndpoint"] = DeviceCategory.Audio,
        ["AudioProcessingObject"] = DeviceCategory.Audio,

        ["Display"] = DeviceCategory.Display,
        ["Monitor"] = DeviceCategory.Display,

        ["USB"] = DeviceCategory.Usb,
        ["USBDevice"] = DeviceCategory.Usb,

        ["Bluetooth"] = DeviceCategory.Bluetooth,
        ["BthLEDevice"] = DeviceCategory.Bluetooth,

        ["DiskDrive"] = DeviceCategory.Storage,
        ["SCSIAdapter"] = DeviceCategory.Storage,
        ["HDC"] = DeviceCategory.Storage,
        ["Volume"] = DeviceCategory.Storage,
        ["VolumeSnapshot"] = DeviceCategory.Storage,
        ["CDROM"] = DeviceCategory.Storage,
        ["SDHost"] = DeviceCategory.Storage,
        ["FloppyDisk"] = DeviceCategory.Storage,
    };

    public static IReadOnlyList<DeviceCategory> All { get; } = Enum.GetValues<DeviceCategory>();

    /// <summary>Menentukan kategori dari nilai kelas PnP (<c>PNPClass</c>), mis. "Net", "MEDIA".</summary>
    public static DeviceCategory FromPnpClass(string? pnpClass) =>
        pnpClass is not null && ByPnpClass.TryGetValue(pnpClass, out var category)
            ? category
            : DeviceCategory.Other;

    public static string DisplayName(this DeviceCategory category) => category switch
    {
        DeviceCategory.Network => "Jaringan",
        DeviceCategory.Audio => "Suara",
        DeviceCategory.Display => "Layar",
        DeviceCategory.Usb => "USB",
        DeviceCategory.Bluetooth => "Bluetooth",
        DeviceCategory.Storage => "Penyimpanan",
        _ => "Lainnya",
    };
}
