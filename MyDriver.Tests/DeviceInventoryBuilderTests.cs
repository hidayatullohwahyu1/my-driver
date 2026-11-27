using MyDriver.Core.Devices;

namespace MyDriver.Tests;

public class DeviceInventoryBuilderTests
{
    private static PnpEntityRecord Entity(
        string id, string? name = "Perangkat", string? pnpClass = null, int? error = 0, bool? present = true,
        string[]? hardwareIds = null) =>
        new(id, name, pnpClass, null, "Produsen", "svc", error, present, hardwareIds, null);

    private static SignedDriverRecord Driver(string id, string? version = "1.0.0.0", string? date = "20230612000000.******+000") =>
        new(id, "Nama dari driver", "NET", version, date, "Intel", "oem42.inf", true, "Microsoft Windows Hardware Compatibility Publisher");

    [Fact]
    public void Build_JoinsDriverDataByDeviceId_CaseInsensitive()
    {
        var devices = DeviceInventoryBuilder.Build(
            [Entity(@"PCI\VEN_8086&DEV_2723\4&1", "Intel Wi-Fi 6 AX200", "Net")],
            [Driver(@"pci\ven_8086&dev_2723\4&1", "22.250.1.2")]);

        var wifi = Assert.Single(devices);
        Assert.Equal("Intel Wi-Fi 6 AX200", wifi.Name);
        Assert.Equal(DeviceCategory.Network, wifi.Category);
        Assert.Equal("22.250.1.2", wifi.DriverVersion);
        Assert.Equal(new DateTime(2023, 6, 12), wifi.DriverDate);
        Assert.Equal("Intel", wifi.DriverProvider);
        Assert.Equal("oem42.inf", wifi.InfName);
        Assert.True(wifi.IsSigned);
        Assert.True(wifi.HasDriver);
    }

    [Fact]
    public void Build_DeviceWithoutDriver_HasNoDriverData()
    {
        var device = Assert.Single(DeviceInventoryBuilder.Build([Entity("USB\\X", error: 28)], []));

        Assert.False(device.HasDriver);
        Assert.Null(device.DriverVersion);
        Assert.Equal(DeviceStatus.Problem, device.Status);
    }

    [Fact]
    public void Build_UsesFallbacks_ForMissingNameAndClass()
    {
        var devices = DeviceInventoryBuilder.Build(
            [Entity("A", name: " ", pnpClass: null), Entity("B", name: null, pnpClass: null)],
            [Driver("A")]);

        var a = devices.Single(d => d.InstanceId == "A");
        var b = devices.Single(d => d.InstanceId == "B");
        Assert.Equal("Nama dari driver", a.Name);
        Assert.Equal("NET", a.PnpClass);
        Assert.Equal(DeviceCategory.Network, a.Category);
        Assert.Equal("B", b.Name);
        Assert.Equal(DeviceCategory.Other, b.Category);
    }

    [Fact]
    public void Build_SkipsEmptyAndDuplicateIds_AndCleansHardwareIds()
    {
        var devices = DeviceInventoryBuilder.Build(
            [Entity(""), Entity("X", hardwareIds: ["  HW\\1 ", "", "HW\\2"]), Entity("x")],
            []);

        var device = Assert.Single(devices);
        Assert.Equal(["HW\\1", "HW\\2"], device.HardwareIds);
    }

    [Fact]
    public void Build_SortsByCategoryThenName()
    {
        var devices = DeviceInventoryBuilder.Build(
            [
                Entity("1", "Zeta", "System"),
                Entity("2", "Speaker", "MEDIA"),
                Entity("3", "Beta WiFi", "Net"),
                Entity("4", "Alpha LAN", "Net"),
            ],
            []);

        Assert.Equal(["Alpha LAN", "Beta WiFi", "Speaker", "Zeta"], devices.Select(d => d.Name));
    }

    [Theory]
    [InlineData(0, true, DeviceStatus.Normal)]
    [InlineData(22, true, DeviceStatus.Disabled)]
    [InlineData(43, true, DeviceStatus.Problem)]
    [InlineData(28, true, DeviceStatus.Problem)]
    [InlineData(45, true, DeviceStatus.NotConnected)]
    [InlineData(0, false, DeviceStatus.NotConnected)]
    public void Status_FollowsErrorCodeAndPresence(int error, bool present, DeviceStatus expected)
    {
        var device = Assert.Single(DeviceInventoryBuilder.Build([Entity("X", error: error, present: present)], []));
        Assert.Equal(expected, device.Status);
    }

    [Theory]
    [InlineData("20230612000000.******+000", 2023, 6, 12)]
    [InlineData("20061231", 2006, 12, 31)]
    public void ParseCimDate_ValidValues(string value, int y, int m, int d) =>
        Assert.Equal(new DateTime(y, m, d), DeviceInventoryBuilder.ParseCimDate(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2023")]
    [InlineData("20231340000000")]
    [InlineData("bukan-tanggal")]
    public void ParseCimDate_InvalidValues_ReturnNull(string? value) =>
        Assert.Null(DeviceInventoryBuilder.ParseCimDate(value));

    [Fact]
    public void ApplySignatures_AddsSignerAndKeepsFastDriverData()
    {
        var fast = DeviceInventoryBuilder.Build(
            [Entity("A"), Entity("B")],
            [new SignedDriverRecord("A", null, null, "2.0", "20240101", "Realtek", "oem1.inf", null, null)]);

        var merged = DeviceInventoryBuilder.ApplySignatures(fast,
        [
            new SignedDriverRecord("a", null, null, "1.0", "20200101", "Lain", "oem9.inf", false, null),
            new SignedDriverRecord("B", null, null, "3.0", "20220202", "Intel", "oem2.inf", true, "Microsoft"),
        ]);

        var a = merged.Single(d => d.InstanceId == "A");
        Assert.False(a.IsSigned);
        Assert.Equal("2.0", a.DriverVersion);          // data cepat tidak ditimpa
        Assert.Equal("oem1.inf", a.InfName);
        var b = merged.Single(d => d.InstanceId == "B");
        Assert.True(b.IsSigned);
        Assert.Equal("Microsoft", b.Signer);
        Assert.Equal("3.0", b.DriverVersion);          // yang kosong dilengkapi
    }
}
