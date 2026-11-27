using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Repair;
using MyDriver.Core.Storage;
using MyDriver.Windows.Commands;
using MyDriver.Windows.Devices;

namespace MyDriver.Tests;

public class ActionPolicyTests
{
    [Theory]
    [InlineData("DiskDrive")]
    [InlineData("SCSIAdapter")]
    [InlineData("HDC")]
    [InlineData("System")]
    [InlineData("Processor")]
    [InlineData("Computer")]
    public void ProtectedClasses_CannotBeUninstalledDisabledOrRestarted(string pnpClass)
    {
        var device = TestData.Device("SYS", "Perangkat sistem") with { PnpClass = pnpClass };

        Assert.True(ActionPolicy.IsProtected(device));
        foreach (var action in new[] { ActionCatalog.UninstallAndRescan, ActionCatalog.DisableEnableDevice, ActionCatalog.RestartDevice, ActionCatalog.ReenableDriverService })
        {
            var check = ActionPolicy.Check(action, device);
            Assert.Equal(ActionPermission.Blocked, check.Permission);
            Assert.Contains("Perangkat sistem", check.Reason);
        }
    }

    [Fact]
    public void ProtectedDevice_RollbackAndBackupAllowedOnlyWithRedWarning()
    {
        var disk = TestData.Device("D", "SSD") with { PnpClass = "DiskDrive" };

        Assert.Equal(ActionPermission.AllowedWithRedWarning, ActionPolicy.Check(ActionCatalog.RollbackToSnapshot, disk).Permission);
        Assert.Equal(ActionPermission.AllowedWithRedWarning, ActionPolicy.Check(ActionCatalog.InstallFromBackup, disk).Permission);
        Assert.Equal(ActionPermission.Allowed, ActionPolicy.Check(ActionCatalog.WindowsUpdate, disk).Permission);
        Assert.Equal(ActionPermission.Allowed, ActionPolicy.Check(ActionCatalog.EnableDevice, disk).Permission);
    }

    [Fact]
    public void NormalDevice_AllActionsAllowed()
    {
        var wifi = TestData.Device("W", "WiFi") with { PnpClass = "Net" };

        Assert.False(ActionPolicy.IsProtected(wifi));
        Assert.All(ActionCatalog.All, a => Assert.Equal(ActionPermission.Allowed, ActionPolicy.Check(a.Id, wifi).Permission));
    }

    [Fact]
    public void RestorePoint_RequiredExactlyForDriverChangingActions()
    {
        var needing = ActionCatalog.All.Where(a => ActionPolicy.NeedsRestorePoint(a.Id)).Select(a => a.Id).Order();

        Assert.Equal(
            new[] { ActionCatalog.InstallFromBackup, ActionCatalog.ReenableDriverService, ActionCatalog.RollbackToSnapshot, ActionCatalog.UninstallAndRescan }.Order(),
            needing);
    }

    [Theory]
    [InlineData(DeviceCategory.Network, "jaringan")]
    [InlineData(DeviceCategory.Audio, "Suara")]
    [InlineData(DeviceCategory.Display, "Layar")]
    public void Impact_NamesTheConsequence(DeviceCategory category, string expected) =>
        Assert.Contains(expected, ActionPolicy.Impact(TestData.Device("X") with { Category = category }));
}

public class InfReaderTests
{
    [Theory]
    [InlineData("[Version]\r\nSignature=\"$WINDOWS NT$\"\r\nDriverVer = 03/05/2023,12.0.0.1272\r\n", "12.0.0.1272")]
    [InlineData("[Version]\nDriverVer=09/12/2023,31.0.15.3742 ; komentar\n", "31.0.15.3742")]
    [InlineData("  driverver   =  06/21/2006 , 10.0.26100.1  \n", "10.0.26100.1")]
    [InlineData("[Version]\nClass=Net\n", null)]
    public void ParseDriverVersion(string text, string? expected) =>
        Assert.Equal(expected, InfReader.ParseDriverVersion(text));

    [Fact]
    public void ReadDriverVersion_HandlesUtf16Files()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "x.inf");
        File.WriteAllText(file, "[Version]\r\nDriverVer=01/02/2024,1.2.3.4\r\n", System.Text.Encoding.Unicode);

        Assert.Equal("1.2.3.4", InfReader.ReadDriverVersion(file));
        Assert.Null(InfReader.ReadDriverVersion(Path.Combine(temp.Path, "tidak-ada.inf")));
    }
}

public class DeviceControlServiceTests
{
    private static CommandResult Result(int exit) => new("pnputil", exit, "", "", TimeSpan.Zero, false);

    [Fact]
    public async Task Restart_RunsPnpUtilWithInstanceId()
    {
        var runner = new FakeCommandRunner();
        var service = new DeviceControlService(runner, () => true);

        var result = await service.RestartAsync(@"PCI\VEN_168C&DEV_0042\1", "WiFi");

        Assert.True(result.Success);
        var call = runner.Calls.Single();
        Assert.Equal("pnputil", call.File);
        Assert.Equal(["/restart-device", @"PCI\VEN_168C&DEV_0042\1"], call.Args);
    }

    [Fact]
    public async Task DisableThenEnable_RetriesEnableOnce_AndReportsFailureClearly()
    {
        var runner = new FakeCommandRunner { Handler = (_, args) => Result(args[0] == "/enable-device" ? 5 : 0) };
        var service = new DeviceControlService(runner, () => true);

        var result = await service.DisableThenEnableAsync("ID", "Kamera");

        Assert.False(result.Success);
        Assert.Contains("GAGAL diaktifkan", result.Message);
        Assert.Equal(["/disable-device", "/enable-device", "/enable-device"], runner.Calls.Select(c => c.Args[0]));
    }

    [Fact]
    public async Task RemoveAndRescan_RescansAfterRemoving()
    {
        var runner = new FakeCommandRunner { Handler = (_, args) => Result(args[0] == "/remove-device" ? 3010 : 0) };
        var service = new DeviceControlService(runner, () => true);

        var result = await service.RemoveAndRescanAsync("ID", "Bluetooth");

        Assert.True(result.Success);
        Assert.True(result.RebootRequired);
        Assert.Equal(["/remove-device", "/scan-devices"], runner.Calls.Select(c => c.Args[0]));
    }

    [Fact]
    public async Task RemoveFails_DoesNotRescan()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => Result(1) };
        var service = new DeviceControlService(runner, () => true);

        var result = await service.RemoveAndRescanAsync("ID", "Bluetooth");

        Assert.False(result.Success);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task OldWindows_DoesNotRunPnpUtil_AndExplainsWhy()
    {
        var runner = new FakeCommandRunner();
        var service = new DeviceControlService(runner, () => false);

        var result = await service.EnableAsync("ID", "WiFi");

        Assert.False(result.Success);
        Assert.Contains("2004", result.Message);
        Assert.Empty(runner.Calls);
    }
}

public class HistoryStoreTests
{
    [Fact]
    public void AppendHistory_KeepsNewestFirst_AndCapsTheList()
    {
        using var temp = new TempDirectory();
        var store = ComputerStore.Open(DataPaths.Resolve(temp.Path), TestData.Computer(), DateTime.Now, new MemoryLog());
        var start = new DateTime(2026, 1, 1);

        IReadOnlyList<HistoryEntry> list = [];
        for (var i = 0; i < ComputerStore.MaxHistoryEntries + 5; i++)
        {
            list = store.AppendHistory(new HistoryEntry { Time = start.AddMinutes(i), Kind = HistoryKind.Scan, Title = $"#{i}" });
        }

        Assert.Equal(ComputerStore.MaxHistoryEntries, list.Count);
        Assert.Equal($"#{ComputerStore.MaxHistoryEntries + 4}", list[0].Title);
        Assert.Equal(list.Select(h => h.Title), store.LoadHistory().Select(h => h.Title));
    }

    [Fact]
    public void History_RoundTripsActionDetails()
    {
        using var temp = new TempDirectory();
        var store = ComputerStore.Open(DataPaths.Resolve(temp.Path), TestData.Computer(), DateTime.Now, new MemoryLog());

        store.AppendHistory(new HistoryEntry
        {
            Time = new DateTime(2026, 11, 27, 10, 0, 0),
            Kind = HistoryKind.Action,
            Title = "Restart perangkat: WiFi",
            Success = false,
            Message = "Gagal (kode keluar 5).",
            ActionId = ActionCatalog.RestartDevice,
            RestorePoint = "Dibuat 27 November 2026, 10.00",
            Commands = [@"C:\Windows\System32\pnputil.exe /restart-device ID"],
        });

        var entry = Assert.Single(store.LoadHistory());
        Assert.Equal(HistoryKind.Action, entry.Kind);
        Assert.False(entry.Success);
        Assert.Equal("Dibuat 27 November 2026, 10.00", entry.RestorePoint);
        Assert.Single(entry.Commands);
    }
}
