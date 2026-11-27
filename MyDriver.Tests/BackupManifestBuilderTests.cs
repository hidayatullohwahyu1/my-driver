using MyDriver.Core.Backups;

namespace MyDriver.Tests;

public class BackupManifestBuilderTests
{
    private static readonly Dictionary<string, string> StoreFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oem1.inf"] = "netathr.inf_amd64_aaa",
        ["oem2.inf"] = "rt640x64.inf_amd64_bbb",
    };

    private static string? Locate(string inf) => StoreFolders.GetValueOrDefault(inf);

    [Fact]
    public void BuildPackages_LinksExportedFoldersToDevicesUsingThem()
    {
        var scan = TestData.Scan(
            TestData.Device("WIFI", "Qualcomm WiFi", inf: "oem1.inf", version: "12.0.0.1272"),
            TestData.Device("LAN", "Realtek LAN", inf: "OEM2.INF", version: "10.1"),
            TestData.Device("LAN2", "Realtek LAN #2", inf: "oem2.inf"),
            TestData.Device("DISK", "Disk bawaan", inf: "disk.inf"));
        ExportedPackage[] exported =
        [
            new("netathr.inf_amd64_aaa", @"netathr.inf_amd64_aaa\netathr.inf"),
            new("rt640x64.inf_amd64_bbb", @"rt640x64.inf_amd64_bbb\rt640x64.inf"),
            new("lama.inf_amd64_ccc", @"lama.inf_amd64_ccc\lama.inf"),
        ];

        var packages = BackupManifestBuilder.BuildPackages(exported, scan, Locate);

        Assert.Equal(3, packages.Count);
        var wifi = packages.Single(p => p.FolderName == "netathr.inf_amd64_aaa");
        Assert.Equal("oem1.inf", wifi.PublishedName);
        Assert.Equal("12.0.0.1272", wifi.Version);
        Assert.Equal("Qualcomm WiFi", wifi.DisplayName);
        Assert.Equal(@"netathr.inf_amd64_aaa\netathr.inf", wifi.InfFile);

        var lan = packages.Single(p => p.FolderName == "rt640x64.inf_amd64_bbb");
        Assert.Equal(["LAN", "LAN2"], lan.Devices.Select(d => d.InstanceId).Order());

        var unused = packages.Last();
        Assert.Equal("lama.inf_amd64_ccc", unused.FolderName);
        Assert.Empty(unused.Devices);
        Assert.Equal("lama.inf", unused.DisplayName);
    }

    [Fact]
    public void FindPackageFor_MatchesInstanceIdFirst_ThenHardwareId()
    {
        var manifest = new BackupManifest
        {
            CreatedAt = DateTime.Now,
            ComputerId = "X",
            Packages =
            [
                new BackupPackage { FolderName = "a", Devices = [new BackupDevice("PCI\\WIFI\\1", "WiFi", ["PCI\\VEN_168C&DEV_0042"])] },
                new BackupPackage { FolderName = "b", Devices = [new BackupDevice("USB\\CAM\\1", "Kamera", ["USB\\VID_0BDA&PID_57FA"])] },
            ],
        };

        Assert.Equal("a", BackupManifestBuilder.FindPackageFor(manifest, TestData.Device("pci\\wifi\\1"))?.FolderName);
        Assert.Equal("b", BackupManifestBuilder.FindPackageFor(manifest,
            TestData.Device("USB\\CAM\\BARU", hardwareIds: ["USB\\VID_0BDA&PID_57FA"]))?.FolderName);
        Assert.Null(BackupManifestBuilder.FindPackageFor(manifest, TestData.Device("LAIN")));
    }

    [Fact]
    public void OldBackups_ReturnsAllBeyondNewestFive()
    {
        var backups = Enumerable.Range(1, 7)
            .Select(d => new BackupInfo($"dir{d}", $"2026-11-0{d}", new DateTime(2026, 11, d), null, 0))
            .Reverse()
            .ToList();

        var old = BackupRetention.OldBackups(backups);

        Assert.Equal(["2026-11-02", "2026-11-01"], old.Select(b => b.Name));
        Assert.Empty(BackupRetention.OldBackups(backups.Take(5)));
    }
}
