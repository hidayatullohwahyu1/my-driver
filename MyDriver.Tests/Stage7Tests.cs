using System.Xml.Linq;
using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Monitoring;
using MyDriver.Core.Reports;
using MyDriver.Core.Repair;
using MyDriver.Core.Snapshots;
using MyDriver.Windows.Drivers;
using MyDriver.Windows.Monitoring;

namespace MyDriver.Tests;

public class MonitorPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 28, 12, 0, 0);

    private static Finding F(string key, FindingLevel level, DeviceInfo? device = null, string title = "Judul") =>
        new() { Key = key, RuleId = key.Split('|')[0], Level = level, Title = title, Explanation = "x", Device = device };

    private static DiagnosisReport Report(params Finding[] findings) => new(Now, findings);

    [Fact]
    public void Safe_DoesNotNotify()
    {
        var decision = MonitorPolicy.Decide(Report(F("driver-old|A", FindingLevel.Info)), null, Now);

        Assert.False(decision.Notify);
        Assert.Equal(OverallStatus.Safe, decision.NewState.LastStatus);
    }

    [Fact]
    public void NewProblem_Notifies_WithDeviceNameLikePrd()
    {
        var wifi = TestData.Device("WIFI", "WiFi");

        var decision = MonitorPolicy.Decide(Report(F("cm-43|WIFI", FindingLevel.Critical, wifi)), null, Now);

        Assert.True(decision.Notify);
        Assert.Equal("My Driver!", decision.Title);
        Assert.Equal("WiFi bermasalah. Klik untuk melihat solusi.", decision.Message);
        Assert.Equal(["cm-43|WIFI"], decision.NewState.NotifiedKeys);
    }

    [Fact]
    public void SameProblemAgain_DoesNotNotifyTwice()
    {
        var report = Report(F("cm-43|WIFI", FindingLevel.Critical, TestData.Device("WIFI", "WiFi")));
        var first = MonitorPolicy.Decide(report, null, Now);

        var second = MonitorPolicy.Decide(report, first.NewState, Now.AddDays(1));

        Assert.False(second.Notify);
        Assert.Empty(second.NewFindings);
    }

    [Fact]
    public void ResolvedThenBack_NotifiesAgain()
    {
        var problem = Report(F("cm-43|WIFI", FindingLevel.Critical, TestData.Device("WIFI", "WiFi")));
        var s1 = MonitorPolicy.Decide(problem, null, Now).NewState;
        var s2 = MonitorPolicy.Decide(Report(), s1, Now.AddDays(1)).NewState;

        Assert.True(MonitorPolicy.Decide(problem, s2, Now.AddDays(2)).Notify);
    }

    [Fact]
    public void SeveralNewProblems_SummarisedInOneNotification()
    {
        var decision = MonitorPolicy.Decide(Report(
            F("backup-missing", FindingLevel.Warning, title: "Belum ada backup driver"),
            F("cm-22|CAM", FindingLevel.Warning, TestData.Device("CAM", "Kamera")),
            F("cm-28|BT", FindingLevel.Critical, TestData.Device("BT", "Bluetooth"))), null, Now);

        Assert.True(decision.Notify);
        Assert.StartsWith("3 masalah baru ditemukan, misalnya: Bluetooth.", decision.Message);
    }
}

public class SnapshotChangeTests
{
    private static readonly RuleSet Rules = RuleSet.LoadEmbedded();
    private static readonly DateTime Now = new(2026, 11, 28, 12, 0, 0);

    private static HealthySnapshot Snapshot(params DeviceInfo[] devices) =>
        HealthySnapshot.FromScan(TestData.Scan(devices), new DateTime(2026, 11, 1));

    private static DiagnosisReport Diagnose(HealthySnapshot snapshot, IReadOnlyDictionary<string, DateTime>? seen, params DeviceInfo[] devices) =>
        new DiagnosisEngine(Rules).Diagnose(new DiagnosisInput(TestData.Scan(devices) with { SignaturesChecked = true }, snapshot, Now.AddDays(-1), Now, seen));

    [Fact]
    public void ChangedDriverOnNormalDevice_IsInfo_WithMarkHealthyFirst()
    {
        var before = TestData.Device("WIFI", "WiFi", version: "1.0");
        var after = before with { DriverVersion = "2.0" };

        var finding = Assert.Single(Diagnose(Snapshot(before), null, after).Findings);

        Assert.Equal("driver-changed", finding.RuleId);
        Assert.Equal(FindingLevel.Info, finding.Level);
        Assert.Contains("1.0 menjadi 2.0", finding.Explanation);
        Assert.Equal([ActionCatalog.MarkHealthy, ActionCatalog.RollbackToSnapshot], finding.Actions.Select(a => a.ActionId));
        Assert.DoesNotContain("tanpa masalah", finding.Explanation);
    }

    [Fact]
    public void ChangedDriver_AfterThreeQuietDays_SuggestsMarkingHealthy()
    {
        var before = TestData.Device("WIFI", "WiFi", version: "1.0");
        var seen = new Dictionary<string, DateTime> { ["driver-changed|WIFI"] = Now.AddDays(-4) };

        var finding = Assert.Single(Diagnose(Snapshot(before), seen, before with { DriverVersion = "2.0" }).Findings);

        Assert.Contains("Sudah 4 hari tanpa masalah", finding.Explanation);
    }

    [Fact]
    public void NewAndMissingDevices_AreInfo()
    {
        var kept = TestData.Device("A", "Keyboard");
        var gone = TestData.Device("B", "Webcam lama");
        var added = TestData.Device("C", "Flashdisk");

        var report = Diagnose(Snapshot(kept, gone), null, kept, added);

        Assert.Equal(2, report.Findings.Count);
        Assert.Equal("Driver baru: Flashdisk", report.Findings.Single(f => f.RuleId == "driver-new").Title);
        var missing = report.Findings.Single(f => f.RuleId == "driver-missing");
        Assert.Equal("Webcam lama tidak terdeteksi lagi", missing.Title);
        Assert.Null(missing.Device);
        Assert.All(report.Findings, f => Assert.Equal(FindingLevel.Info, f.Level));
        Assert.Equal(OverallStatus.Safe, report.Status);
    }

    [Fact]
    public void ProblemDevice_IsNotReportedAsChange_ItGetsTheRollbackRuleInstead()
    {
        var before = TestData.Device("GPU", version: "30.0");

        var finding = Assert.Single(Diagnose(Snapshot(before), null, before with { DriverVersion = "31.0", ErrorCode = 43 }).Findings);

        Assert.Equal("cm-43", finding.RuleId);
        Assert.NotNull(finding.ChangeNote);
    }

    [Fact]
    public void WithDevice_ReplacesOnlyThatDevicesEntry()
    {
        var a = TestData.Device("A", version: "1.0");
        var b = TestData.Device("B", version: "5.0");

        var updated = Snapshot(a, b).WithDevice(a with { DriverVersion = "2.0" });

        Assert.Equal(2, updated.Drivers.Count);
        Assert.Equal("2.0", updated.Drivers.Single(d => d.InstanceId == "A").DriverVersion);
        Assert.Equal("5.0", updated.Drivers.Single(d => d.InstanceId == "B").DriverVersion);
    }
}

public class ReportBuilderTests
{
    private static ReportInput Input()
    {
        var bad = TestData.Device("PCI\\BAD", "Kartu <Grafis> & Co", error: 43);
        var ok = TestData.Device("PCI\\OK", "WiFi");
        var scan = TestData.Scan(bad, ok);
        var diagnosis = new DiagnosisEngine(RuleSet.LoadEmbedded()).Diagnose(
            new DiagnosisInput(scan, null, new DateTime(2026, 11, 20), new DateTime(2026, 11, 28)));
        return new ReportInput(scan, diagnosis, new DateTime(2026, 11, 28, 9, 30, 0), "0.1.0", new DateTime(2026, 11, 20));
    }

    [Fact]
    public void Html_ContainsStatusFindingsDevices_AndEscapesNames()
    {
        var html = ReportBuilder.Html(Input());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("Bermasalah", html);
        Assert.Contains("Kartu &lt;Grafis&gt; &amp; Co dihentikan Windows", html);
        Assert.DoesNotContain("<Grafis>", html);
        Assert.Contains("X442URR", html);
        Assert.Contains("28 November 2026, 09.30", html);
        Assert.Contains("Restart perangkat", html);
        Assert.Contains("class=\"bad\"", html);
    }

    [Fact]
    public void Text_ListsFindingsWithNumberedStepsAndAllDevices()
    {
        var text = ReportBuilder.Text(Input());

        Assert.Contains("STATUS        : Bermasalah", text);
        Assert.Contains("[Bermasalah] Kartu <Grafis> & Co dihentikan Windows", text);
        Assert.Contains("  1. Restart perangkat", text);
        Assert.Contains("WiFi", text);
        Assert.Contains("Backup terakhir: 20 November 2026", text);
    }
}

public class InfFieldTests
{
    private const string Inf = """
        [Version]
        Signature   = "$WINDOWS NT$"
        Class       = Net
        ClassGuid   = {4d36e972-e325-11ce-bfc1-08002be10318}
        Provider    = %ManufacturerName% ; komentar
        DriverVer   = 03/05/2023,12.0.0.1272

        [Strings]
        ManufacturerName = "Qualcomm Atheros Communications Inc."
        """;

    [Theory]
    [InlineData("Class", "Net")]
    [InlineData("Provider", "Qualcomm Atheros Communications Inc.")]
    [InlineData("Signature", "$WINDOWS NT$")]
    [InlineData("CatalogFile", null)]
    public void ParseVersionField_ResolvesStringTokens(string key, string? expected) =>
        Assert.Equal(expected, InfReader.ParseVersionField(Inf, key));
}

public class ScheduledTaskTests
{
    [Fact]
    public void TaskXml_IsValid_RunsBackgroundScanAtLogonAndDaily()
    {
        var xml = ScheduledTaskService.BuildTaskXml(@"D:\Alat & Driver\MyDriver.exe", 21, @"LAPTOP\hiday");

        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.NotNull(doc.Descendants(ns + "LogonTrigger").SingleOrDefault());
        Assert.EndsWith("T21:00:00", doc.Descendants(ns + "StartBoundary").Single().Value);
        Assert.Equal("\"D:\\Alat & Driver\\MyDriver.exe\"", doc.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--background", doc.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("true", doc.Descendants(ns + "StartWhenAvailable").Single().Value);
        Assert.Equal(@"LAPTOP\hiday", doc.Descendants(ns + "UserId").First().Value);
    }

    [Theory]
    [InlineData(-5, "T00:00:00")]
    [InlineData(30, "T23:00:00")]
    public void TaskXml_ClampsHour(int hour, string expected) =>
        Assert.Contains(expected, ScheduledTaskService.BuildTaskXml(@"C:\x\MyDriver.exe", hour, "u"));
}

public class DriverStoreInventoryTests
{
    [Fact]
    public void Delete_NeverUsesForce() =>
        Assert.Equal(["/delete-driver", "oem12.inf"], DriverStoreInventory.DeleteArguments("oem12.inf"));

    [Fact]
    public async Task Delete_SkipsPackagesInUseOrProtected()
    {
        var runner = new FakeCommandRunner();
        var inventory = new DriverStoreInventory(runner, new MemoryLog());
        DriverStorePackage[] packages =
        [
            new() { PublishedName = "oem1.inf", OriginalName = "a.inf", InUse = true },
            new() { PublishedName = "oem2.inf", OriginalName = "b.inf", IsProtectedClass = true },
            new() { PublishedName = "oem3.inf", OriginalName = "c.inf" },
        ];

        var (deleted, failures, _) = await inventory.DeleteAsync(packages);

        Assert.Equal(1, deleted);
        Assert.Empty(failures);
        Assert.Equal("oem3.inf", runner.Calls.Single().Args[1]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task List_RealDriverStore_MarksPackagesUsedByDevices()
    {
        var log = new MemoryLog();
        var scan = await new MyDriver.Windows.Wmi.WmiDeviceScanner(new MyDriver.Windows.Wmi.WmiService(log), log).ScanAsync();

        var packages = await new DriverStoreInventory(new FakeCommandRunner(), log).ListAsync(scan.Devices);

        Assert.NotEmpty(packages);
        Assert.Contains(packages, p => p.InUse);
        Assert.All(packages, p => Assert.StartsWith("oem", p.PublishedName, StringComparison.OrdinalIgnoreCase));
        var used = scan.Devices.Select(d => d.InfName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(packages.Where(p => p.CanOfferForRemoval), p => Assert.DoesNotContain(p.PublishedName, used));
    }
}
