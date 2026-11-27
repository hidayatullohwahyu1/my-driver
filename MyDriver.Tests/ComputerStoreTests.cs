using MyDriver.Core.Backups;
using MyDriver.Core.Devices;
using MyDriver.Core.Snapshots;
using MyDriver.Core.Storage;

namespace MyDriver.Tests;

public class ComputerStoreTests
{
    private static (TempDirectory Temp, DataPaths Paths) NewData()
    {
        var temp = new TempDirectory();
        return (temp, DataPaths.Resolve(temp.Path));
    }

    [Fact]
    public void Open_NewComputer_CreatesFolderAndProfile()
    {
        var (temp, paths) = NewData();
        using var _ = temp;

        var store = ComputerStore.Open(paths, TestData.Computer(), new DateTime(2026, 11, 27), new MemoryLog());

        Assert.True(store.IsNew);
        Assert.StartsWith("ASUS_X442URR_", Path.GetFileName(store.Directory));
        Assert.True(File.Exists(store.ProfileFile));
        Assert.Equal("X442URR", store.Profile.Model);
        Assert.False(store.Profile.OnboardingCompleted);
    }

    [Fact]
    public void Open_SameHardware_ReusesFolderEvenIfModelNameChanged()
    {
        var (temp, paths) = NewData();
        using var _ = temp;
        var first = ComputerStore.Open(paths, TestData.Computer(), new DateTime(2026, 11, 1), new MemoryLog());
        first.Profile.OnboardingCompleted = true;
        first.SaveProfile();

        var renamed = TestData.Computer() with { Model = "VivoBook X442URR" };
        var second = ComputerStore.Open(paths, renamed, new DateTime(2026, 11, 27), new MemoryLog());

        Assert.False(second.IsNew);
        Assert.Equal(first.Directory, second.Directory);
        Assert.True(second.Profile.OnboardingCompleted);
        Assert.Equal(new DateTime(2026, 11, 1), second.Profile.FirstSeen);
        Assert.Equal(new DateTime(2026, 11, 27), second.Profile.LastSeen);
        Assert.Single(Directory.GetDirectories(paths.ComputersDirectory));
    }

    [Fact]
    public void Open_DifferentComputer_GetsOwnFolder()
    {
        var (temp, paths) = NewData();
        using var _ = temp;

        var a = ComputerStore.Open(paths, TestData.Computer(serial: "AAA"), DateTime.Now, new MemoryLog());
        var b = ComputerStore.Open(paths, TestData.Computer(serial: "BBB"), DateTime.Now, new MemoryLog());

        Assert.NotEqual(a.Directory, b.Directory);
    }

    [Fact]
    public void Snapshot_RoundTrips_AndContainsOnlyHealthyDevicesWithDriver()
    {
        var (temp, paths) = NewData();
        using var _ = temp;
        var store = ComputerStore.Open(paths, TestData.Computer(), DateTime.Now, new MemoryLog());
        var scan = TestData.Scan(
            TestData.Device("OK", "WiFi", hardwareIds: ["PCI\\VEN_168C&DEV_0042"]),
            TestData.Device("ERR", "Kamera", error: 43),
            TestData.Device("OFF", "Bluetooth", error: 22),
            TestData.Device("NODRV", "Tanpa driver", inf: null, version: null));

        store.SaveSnapshot(HealthySnapshot.FromScan(scan, new DateTime(2026, 11, 27, 15, 0, 0)));
        var loaded = store.LoadSnapshot();

        Assert.NotNull(loaded);
        var driver = Assert.Single(loaded.Drivers);
        Assert.Equal("WiFi", driver.Name);
        Assert.Equal("1.0", driver.DriverVersion);
        Assert.Equal(new DateTime(2023, 3, 5), driver.DriverDate);
        Assert.Equal(["PCI\\VEN_168C&DEV_0042"], driver.HardwareIds);
    }

    [Fact]
    public void LoadSnapshot_CorruptFile_ReturnsNull()
    {
        var (temp, paths) = NewData();
        using var _ = temp;
        var store = ComputerStore.Open(paths, TestData.Computer(), DateTime.Now, new MemoryLog());
        File.WriteAllText(store.SnapshotFile, "{ rusak");

        Assert.Null(store.LoadSnapshot());
    }

    [Fact]
    public void SaveScan_WritesTimestampedFile_ThatRoundTrips()
    {
        var (temp, paths) = NewData();
        using var _ = temp;
        var store = ComputerStore.Open(paths, TestData.Computer(), DateTime.Now, new MemoryLog());
        var scan = TestData.Scan(TestData.Device("A", "WiFi", error: 43));

        var file = store.SaveScan(scan);

        Assert.Equal("2026-11-27_1500.json", Path.GetFileName(file));
        var loaded = JsonFiles.Read<ScanResult>(file)!;
        var device = Assert.Single(loaded.Devices);
        Assert.Equal(43, device.ErrorCode);
        Assert.Equal(DeviceStatus.Problem, device.Status);
        Assert.Equal("X442URR", loaded.Computer.Model);
    }

    [Fact]
    public void SaveScan_KeepsOnlyNewestHundred()
    {
        var (temp, paths) = NewData();
        using var _ = temp;
        var store = ComputerStore.Open(paths, TestData.Computer(), DateTime.Now, new MemoryLog());
        var start = new DateTime(2026, 1, 1);

        for (var i = 0; i < ComputerStore.MaxSavedScans + 3; i++)
        {
            store.SaveScan(TestData.Scan() with { ScannedAt = start.AddMinutes(i) });
        }

        var files = Directory.GetFiles(store.ScansDirectory).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(ComputerStore.MaxSavedScans, files.Count);
        Assert.Equal("2026-01-01_0003.json", files[0]);
    }

    [Fact]
    public void CreateBackupDirectory_SecondBackupSameDay_GetsTimeSuffix()
    {
        var (temp, paths) = NewData();
        using var _ = temp;
        var store = ComputerStore.Open(paths, TestData.Computer(), DateTime.Now, new MemoryLog());

        var first = store.CreateBackupDirectory(new DateTime(2026, 11, 26, 15, 0, 0));
        var second = store.CreateBackupDirectory(new DateTime(2026, 11, 26, 16, 30, 5));

        Assert.Equal("2026-11-26", Path.GetFileName(first));
        Assert.Equal("2026-11-26_163005", Path.GetFileName(second));
    }

    [Fact]
    public void ListBackups_ReadsManifests_NewestFirst_AndMarksIncomplete()
    {
        var (temp, paths) = NewData();
        using var _ = temp;
        var store = ComputerStore.Open(paths, TestData.Computer(), DateTime.Now, new MemoryLog());
        var older = store.CreateBackupDirectory(new DateTime(2026, 10, 1));
        store.SaveBackupManifest(older, new BackupManifest
        {
            CreatedAt = new DateTime(2026, 10, 1), ComputerId = store.Profile.Id, TotalBytes = 1000,
            Packages = [new BackupPackage { FolderName = "a.inf_amd64_1" }],
        });
        var incomplete = store.CreateBackupDirectory(new DateTime(2026, 11, 1));
        File.WriteAllText(Path.Combine(incomplete, "sisa.txt"), "12345");

        var backups = store.ListBackups();

        Assert.Equal(2, backups.Count);
        Assert.Equal("2026-10-01", backups.Single(b => b.IsComplete).Name);
        Assert.Equal(1, backups.Single(b => b.IsComplete).PackageCount);
        Assert.Equal(5, backups.Single(b => !b.IsComplete).SizeBytes);
    }

    [Fact]
    public void DeleteBackup_RefusesFoldersOutsideBackups()
    {
        var (temp, paths) = NewData();
        using var _ = temp;
        var store = ComputerStore.Open(paths, TestData.Computer(), DateTime.Now, new MemoryLog());
        var outside = new BackupInfo(paths.Root, "MyDriverData", DateTime.Now, null, 0);

        Assert.Throws<InvalidOperationException>(() => store.DeleteBackup(outside));
        Assert.True(Directory.Exists(paths.Root));

        var inside = store.CreateBackupDirectory(DateTime.Now);
        store.DeleteBackup(new BackupInfo(inside, "x", DateTime.Now, null, 0));
        Assert.False(Directory.Exists(inside));
    }
}
