using MyDriver.Core.Devices;

namespace MyDriver.Tests;

public class DeviceCategoryTests
{
    [Theory]
    [InlineData("Net", DeviceCategory.Network)]
    [InlineData("net", DeviceCategory.Network)]
    [InlineData("MEDIA", DeviceCategory.Audio)]
    [InlineData("AudioEndpoint", DeviceCategory.Audio)]
    [InlineData("Display", DeviceCategory.Display)]
    [InlineData("Monitor", DeviceCategory.Display)]
    [InlineData("USB", DeviceCategory.Usb)]
    [InlineData("Bluetooth", DeviceCategory.Bluetooth)]
    [InlineData("DiskDrive", DeviceCategory.Storage)]
    [InlineData("SCSIAdapter", DeviceCategory.Storage)]
    [InlineData("HDC", DeviceCategory.Storage)]
    [InlineData("System", DeviceCategory.Other)]
    [InlineData("Camera", DeviceCategory.Other)]
    [InlineData(null, DeviceCategory.Other)]
    public void FromPnpClass_MapsKnownClasses(string? pnpClass, DeviceCategory expected) =>
        Assert.Equal(expected, DeviceCategories.FromPnpClass(pnpClass));

    [Fact]
    public void DisplayName_IsIndonesianAndCoversEveryCategory()
    {
        Assert.Equal(
            ["Jaringan", "Suara", "Layar", "USB", "Bluetooth", "Penyimpanan", "Lainnya"],
            DeviceCategories.All.Select(c => c.DisplayName()));
    }
}
