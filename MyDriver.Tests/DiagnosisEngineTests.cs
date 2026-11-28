using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Snapshots;

namespace MyDriver.Tests;

public class DiagnosisEngineTests
{
    private static readonly RuleSet Rules = RuleSet.LoadEmbedded();
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0);
    private static readonly DateTime RecentBackup = Now.AddDays(-3);

    private static DiagnosisReport Diagnose(
        ScanResult scan, HealthySnapshot? snapshot = null, DateTime? lastBackup = null, bool noBackup = false,
        IReadOnlyDictionary<string, DateTime>? seen = null) =>
        new DiagnosisEngine(Rules).Diagnose(new DiagnosisInput(
            scan, snapshot, noBackup ? null : lastBackup ?? RecentBackup, Now, seen));

    private static ScanResult Scan(params DeviceInfo[] devices) =>
        TestData.Scan(devices) with { SignaturesChecked = true };

    private static Finding Single(DiagnosisReport report) => Assert.Single(report.Findings);

    /// <summary>Tabel kode error dari PRD: tingkat dan urutan rekomendasi.</summary>
    public static TheoryData<int, FindingLevel, string[]> ErrorCodeTable => new()
    {
        { 1, FindingLevel.Critical, ["installFromBackup", "windowsUpdate", "uninstallAndRescan"] },
        { 3, FindingLevel.Critical, ["restartWindows", "uninstallAndRescan", "installFromBackup"] },
        { 10, FindingLevel.Critical, ["restartDevice", "rollbackToSnapshot", "windowsUpdate", "uninstallAndRescan"] },
        { 12, FindingLevel.Critical, ["restartWindows", "disableUnusedDevices", "checkBios"] },
        { 14, FindingLevel.Warning, ["restartWindows"] },
        { 18, FindingLevel.Critical, ["uninstallAndRescan", "installFromBackup"] },
        { 19, FindingLevel.Critical, ["uninstallAndRescan", "systemRestore"] },
        { 22, FindingLevel.Warning, ["enableDevice"] },
        { 24, FindingLevel.Warning, ["checkConnection", "rescanHardware"] },
        { 28, FindingLevel.Critical, ["installFromBackup", "windowsUpdate", "copyHardwareId"] },
        { 31, FindingLevel.Critical, ["restartDevice", "rollbackToSnapshot", "windowsUpdate"] },
        { 32, FindingLevel.Critical, ["reenableDriverService", "uninstallAndRescan"] },
        { 37, FindingLevel.Critical, ["rollbackToSnapshot", "installFromBackup", "windowsUpdate"] },
        { 39, FindingLevel.Critical, ["rollbackToSnapshot", "installFromBackup", "windowsUpdate"] },
        { 43, FindingLevel.Critical, ["restartDevice", "rollbackToSnapshot", "windowsUpdate", "checkHardware"] },
        { 48, FindingLevel.Critical, ["windowsUpdate", "manufacturerSite"] },
        { 52, FindingLevel.Critical, ["windowsUpdate", "installFromBackup", "manufacturerSite"] },
        { 77, FindingLevel.Warning, ["copyHardwareId", "windowsUpdate"] },
    };

    [Theory]
    [MemberData(nameof(ErrorCodeTable))]
    public void DeviceErrorCode_ProducesExpectedLevelAndRecommendations(int code, FindingLevel level, string[] actions)
    {
        var finding = Single(Diagnose(Scan(TestData.Device("DEV", "WiFi", error: code))));

        Assert.Equal(level, finding.Level);
        Assert.Equal(actions, finding.Actions.Select(a => a.ActionId));
        Assert.Contains("WiFi", finding.Title);
        Assert.False(finding.HiddenByDefault);
        Assert.Equal("DEV", finding.Device?.InstanceId);
        Assert.DoesNotContain("{", finding.Title + finding.Explanation);
    }

    [Fact]
    public void Code43_UsesPrdTitleWithoutTechnicalTerms()
    {
        var finding = Single(Diagnose(Scan(TestData.Device("GPU", "NVIDIA GeForce 930MX", error: 43))));

        Assert.Equal("cm-43", finding.RuleId);
        Assert.Equal("NVIDIA GeForce 930MX dihentikan Windows", finding.Title);
        Assert.DoesNotContain("43", finding.Title); // kode teknis hanya di "Detail teknis"
    }

    [Fact]
    public void UnknownCode_ShowsCodeInExplanation()
    {
        var finding = Single(Diagnose(Scan(TestData.Device("X", "Kamera", error: 77))));

        Assert.Equal("cm-other", finding.RuleId);
        Assert.Contains("77", finding.Explanation);
    }

    [Fact]
    public void NotConnected_IsHiddenInfo_AndDoesNotAffectStatus()
    {
        var report = Diagnose(Scan(
            TestData.Device("A", "Flashdisk lama", error: 45),
            TestData.Device("B", "Mouse lama", present: false)));

        Assert.All(report.Findings, f =>
        {
            Assert.Equal("cm-45", f.RuleId);
            Assert.Equal(FindingLevel.Info, f.Level);
            Assert.True(f.HiddenByDefault);
            Assert.Empty(f.Actions);
        });
        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(OverallStatus.Safe, report.Status);
    }

    [Fact]
    public void Status_IsProblemWithAnyRed_AttentionWithYellow_ElseSafe()
    {
        Assert.Equal(OverallStatus.Safe, Diagnose(Scan(TestData.Device("A"))).Status);
        Assert.Equal(OverallStatus.Attention, Diagnose(Scan(TestData.Device("A", error: 22))).Status);
        Assert.Equal(OverallStatus.Problem, Diagnose(Scan(TestData.Device("A", error: 22), TestData.Device("B", error: 43))).Status);
    }

    [Fact]
    public void Findings_AreOrderedRedThenYellowThenInfo()
    {
        var report = Diagnose(Scan(
            TestData.Device("A", "A", error: 45),
            TestData.Device("B", "B", error: 22),
            TestData.Device("C", "C", error: 43)));

        Assert.Equal([FindingLevel.Critical, FindingLevel.Warning, FindingLevel.Info], report.Findings.Select(f => f.Level));
    }

    // ---- Aturan cerdas "perubahan + masalah" ----

    private static HealthySnapshot SnapshotWith(string instanceId, string? version, string[]? hardwareIds = null) =>
        new(new DateTime(2026, 11, 1), [new SnapshotDriver(instanceId, "WiFi", "Net", DeviceCategory.Network,
            hardwareIds ?? [$"HW\\{instanceId}"], version, null, "Intel", "oem1.inf")]);

    [Fact]
    public void ChangedDriverOnProblemDevice_PutsRollbackFirst_WithNote()
    {
        var device = TestData.Device("WIFI", "WiFi", error: 28, version: "2.0");

        var finding = Single(Diagnose(Scan(device), SnapshotWith("WIFI", "1.0")));

        Assert.Equal(ActionCatalog.RollbackToSnapshot, finding.Actions[0].ActionId);
        Assert.Equal(["rollbackToSnapshot", "installFromBackup", "windowsUpdate", "copyHardwareId"], finding.Actions.Select(a => a.ActionId));
        Assert.Contains("1.0", finding.ChangeNote);
        Assert.Contains("2.0", finding.ChangeNote);
        Assert.Contains("1 November 2026", finding.ChangeNote);
    }

    [Fact]
    public void ChangedDriver_MovesExistingRollbackToTop_WithoutDuplicates()
    {
        var finding = Single(Diagnose(Scan(TestData.Device("GPU", error: 43, version: "31.0")), SnapshotWith("GPU", "30.0")));

        Assert.Equal(["rollbackToSnapshot", "restartDevice", "windowsUpdate", "checkHardware"], finding.Actions.Select(a => a.ActionId));
    }

    [Fact]
    public void DriverRemoved_CountsAsChanged()
    {
        var finding = Single(Diagnose(Scan(TestData.Device("CAM", error: 28, inf: null, version: null)), SnapshotWith("CAM", "5.0")));

        Assert.Equal(ActionCatalog.RollbackToSnapshot, finding.Actions[0].ActionId);
        Assert.Contains("tidak ada driver", finding.ChangeNote);
    }

    [Fact]
    public void SnapshotMatchedByHardwareId_WhenInstanceIdChanged()
    {
        var device = TestData.Device("USB\\NEW\\2", error: 43, version: "2.0", hardwareIds: ["USB\\VID_1&PID_2"]);

        var finding = Single(Diagnose(Scan(device), SnapshotWith("USB\\OLD\\1", "1.0", ["USB\\VID_1&PID_2"])));

        Assert.Equal(ActionCatalog.RollbackToSnapshot, finding.Actions[0].ActionId);
    }

    [Theory]
    [InlineData(43, "1.0")] // versi sama → tidak berubah
    [InlineData(22, "2.0")] // dinonaktifkan bukan "bermasalah"
    [InlineData(14, "2.0")] // tinggal restart Windows
    public void ChangeRule_DoesNotApply(int code, string currentVersion)
    {
        var finding = Single(Diagnose(Scan(TestData.Device("WIFI", error: code, version: currentVersion)), SnapshotWith("WIFI", "1.0")));

        Assert.Null(finding.ChangeNote);
        Assert.NotEqual(ActionCatalog.RollbackToSnapshot, finding.Actions.FirstOrDefault()?.ActionId);
    }

    [Fact]
    public void ChangeRule_NeedsSnapshot()
    {
        var finding = Single(Diagnose(Scan(TestData.Device("WIFI", error: 28, version: "2.0"))));

        Assert.Null(finding.ChangeNote);
        Assert.Equal(ActionCatalog.InstallFromBackup, finding.Actions[0].ActionId);
    }

    // ---- Kondisi driver dan backup ----

    [Fact]
    public void UnsignedDriver_IsYellow_OnlyAfterSignaturesChecked()
    {
        var device = TestData.Device("X", "Pembaca kartu") with { IsSigned = false };

        Assert.Empty(Diagnose(TestData.Scan(device)).Findings); // tanda tangan belum diperiksa
        var finding = Single(Diagnose(Scan(device)));
        Assert.Equal("driver-unsigned", finding.RuleId);
        Assert.Equal(FindingLevel.Warning, finding.Level);
        Assert.Equal([ActionCatalog.WindowsUpdate], finding.Actions.Select(a => a.ActionId));
    }

    [Fact]
    public void OldThirdPartyDriver_IsInfo_ButInboxDriversAreIgnored()
    {
        var oldOem = TestData.Device("A", "Realtek LAN", inf: "oem7.inf") with { DriverDate = new DateTime(2017, 10, 8) };
        var oldInbox = TestData.Device("B", "Disk", inf: "disk.inf") with { DriverDate = new DateTime(2006, 6, 21) };
        var recent = TestData.Device("C", "WiFi", inf: "oem8.inf") with { DriverDate = new DateTime(2023, 3, 5) };

        var finding = Single(Diagnose(Scan(oldOem, oldInbox, recent)));

        Assert.Equal("driver-old", finding.RuleId);
        Assert.Equal(FindingLevel.Info, finding.Level);
        Assert.Contains("9 tahun lalu", finding.Explanation);
        Assert.Equal(OverallStatus.Safe, Diagnose(Scan(oldOem)).Status);
    }

    [Fact]
    public void BackupMissing_IsYellow()
    {
        var finding = Single(Diagnose(Scan(TestData.Device("A")), noBackup: true));

        Assert.Equal("backup-missing", finding.RuleId);
        Assert.Equal(FindingLevel.Warning, finding.Level);
        Assert.Null(finding.Device);
        Assert.Equal([ActionCatalog.BackupNow], finding.Actions.Select(a => a.ActionId));
    }

    [Theory]
    [InlineData(91, true)]
    [InlineData(89, false)]
    public void BackupOlderThan90Days_IsYellow(int ageDays, bool expected)
    {
        var report = Diagnose(Scan(TestData.Device("A")), lastBackup: Now.AddDays(-ageDays));

        Assert.Equal(expected, report.Findings.Any(f => f.RuleId == "backup-old"));
        if (expected)
        {
            Assert.Contains($"{ageDays} hari lalu", report.Findings.Single().Explanation);
        }
    }

    [Fact]
    public void FirstSeen_IsKeptFromHistory_AndNewFindingsUseNow()
    {
        var earlier = new DateTime(2026, 11, 20, 8, 0, 0);
        var report = Diagnose(
            Scan(TestData.Device("A", error: 43), TestData.Device("B", error: 22)),
            seen: new Dictionary<string, DateTime> { ["cm-43|A"] = earlier, ["cm-10|LAMA"] = earlier });

        Assert.Equal(earlier, report.Findings.Single(f => f.Key == "cm-43|A").FirstSeen);
        Assert.Equal(Now, report.Findings.Single(f => f.Key == "cm-22|B").FirstSeen);
        Assert.Equal(["cm-22|B", "cm-43|A"], report.FirstSeenMap().Keys.Order()); // temuan yang sudah selesai dibuang
    }

    /// <summary>Setiap aturan di rules.json harus terpicu oleh data contoh di atas (tidak ada aturan yang tak teruji).</summary>
    [Fact]
    public void EveryRuleInRulesJson_IsTriggeredBySampleData()
    {
        var devices = ErrorCodeTable.Select(row => (int)row[0])
            .Append(45)
            .Select(code => TestData.Device($"E{code}", $"Perangkat {code}", error: code))
            .Append(TestData.Device("U", "Tanpa tanda tangan") with { IsSigned = false })
            .Append(TestData.Device("O", "Driver tua", inf: "oem9.inf") with { DriverDate = new DateTime(2015, 1, 1) })
            .ToArray();

        var triggered = Diagnose(Scan(devices), noBackup: true).Findings
            .Concat(Diagnose(Scan(), lastBackup: Now.AddDays(-200)).Findings)
            .Concat(new DiagnosisEngine(Rules).Diagnose(new DiagnosisInput(Scan(), null, RecentBackup, Now, Health: SystemHealthTests.SampleHealth(Now))).Findings)
            .Concat(Diagnose(
                Scan(TestData.Device("SAMA", version: "2.0"), TestData.Device("BARU")),
                SnapshotWith("SAMA", "1.0") with { Drivers = [.. SnapshotWith("SAMA", "1.0").Drivers, .. SnapshotWith("HILANG", "1.0").Drivers] }).Findings)
            .Select(f => f.RuleId)
            .ToHashSet();

        var untested = Rules.Rules.Select(r => r.Id).Where(id => !triggered.Contains(id)).ToList();
        Assert.True(untested.Count == 0, $"Aturan tanpa data uji: {string.Join(", ", untested)}");
    }
}
