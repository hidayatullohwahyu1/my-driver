using MyDriver.Core.Devices;

namespace MyDriver.Tests;

/// <summary>Data perangkat contoh untuk test.</summary>
internal static class TestData
{
    public static DeviceInfo Device(
        string id, string name = "Perangkat", int error = 0, string? inf = "oem1.inf", string? version = "1.0",
        string[]? hardwareIds = null, DeviceCategory category = DeviceCategory.Other, bool present = true) =>
        new()
        {
            InstanceId = id,
            Name = name,
            ErrorCode = error,
            InfName = inf,
            DriverVersion = version,
            DriverProvider = "Produsen",
            DriverDate = new DateTime(2023, 3, 5),
            PnpClass = "Net",
            Category = category,
            HardwareIds = hardwareIds ?? [$"HW\\{id}"],
            IsPresent = present,
        };

    public static ComputerInfo Computer(string? uuid = "4C4C4544-0035-3010-8052-B4C04F4E4C32", string? serial = "J1N0CV12345") =>
        new()
        {
            ComputerName = "LAPTOP-UJI",
            Manufacturer = "ASUSTeK COMPUTER INC.",
            Model = "X442URR",
            SerialNumber = serial,
            HardwareUuid = uuid,
            OsName = "Windows 11 Home",
        };

    public static ScanResult Scan(params DeviceInfo[] devices) =>
        new(new DateTime(2026, 11, 27, 15, 0, 0), TimeSpan.FromSeconds(2), Computer(), devices);
}
