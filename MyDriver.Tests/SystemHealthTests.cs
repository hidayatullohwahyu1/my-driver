using System.Buffers.Binary;
using System.Text;
using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;
using MyDriver.Windows.Health;
using MyDriver.Windows.Update;

namespace MyDriver.Tests;

public class SystemHealthTests
{
    private static readonly RuleSet Rules = RuleSet.LoadEmbedded();
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0);

    private static SystemEvent Event(string provider, int id, DateTime time, int level = 4, params string[] props) =>
        new(time, provider, id, level, props);

    /// <summary>Contoh data yang memicu setiap aturan log kejadian.</summary>
    internal static SystemHealth SampleHealth(DateTime now) => new()
    {
        Loaded = true,
        Events =
        [
            new(now.AddDays(-1), SystemEvent.DisplayProvider, 4101, 3, ["nvlddmkm"]),
            new(now.AddDays(-1), SystemEvent.KernelPnpProvider, 219, 3, ["24", @"ACPI\INT3400\2&daba3ff&0", "3221226341", "14", @"\Driver\WUDFRd", "0"]),
            new(now.AddDays(-2), SystemEvent.WheaProvider, 18, 2, []),
            new(now.AddDays(-3), SystemEvent.KernelPowerProvider, 41, 1, ["0"]),
        ],
        Crashes =
        [
            new CrashReport { Time = now.AddDays(-1), BugCheckCode = 0x9F, SuspectDriver = "netwtw14.sys", ReportId = "a" },
            new CrashReport { Time = now.AddDays(-2), BugCheckCode = 0x12B, ReportId = "b" },
        ],
    };

    private static DiagnosisReport Diagnose(SystemHealth health, params DeviceInfo[] devices) =>
        new DiagnosisEngine(Rules).Diagnose(new DiagnosisInput(
            TestData.Scan(devices) with { SignaturesChecked = true }, null, Now.AddDays(-1), Now, Health: health));

    private static DeviceInfo Gpu => TestData.Device(@"PCI\VEN_10DE&DEV_134E\4&1", "NVIDIA GeForce 930MX") with
    {
        Service = "nvlddmkm", Category = DeviceCategory.Display, PnpClass = "Display",
    };

    private static DeviceInfo Wifi => TestData.Device(@"PCI\VEN_8086&DEV_2723\4&2", "Intel Wi-Fi 6 AX200") with
    {
        Service = "Netwtw14", Category = DeviceCategory.Network,
    };

    [Theory]
    [InlineData(1, FindingLevel.Warning)]
    [InlineData(2, FindingLevel.Warning)]
    [InlineData(3, FindingLevel.Critical)]
    public void Tdr_IsYellow_AndRedFromThreeTimesInSevenDays(int count, FindingLevel expected)
    {
        var events = Enumerable.Range(0, count)
            .Select(i => Event(SystemEvent.DisplayProvider, 4101, Now.AddDays(-i - 1), 3, "nvlddmkm"))
            .Append(Event(SystemEvent.DisplayProvider, 4101, Now.AddDays(-10), 3, "nvlddmkm")) // di luar 7 hari
            .ToList();

        var finding = Assert.Single(Diagnose(new SystemHealth { Events = events, Loaded = true }, Gpu).Findings);

        Assert.Equal("tdr", finding.RuleId);
        Assert.Equal(expected, finding.Level);
        Assert.Equal(Gpu.InstanceId, finding.Device?.InstanceId);
        Assert.Contains($"{count} kali", finding.Explanation);
        Assert.Equal(ActionCatalog.RollbackToSnapshot, finding.Actions[0].ActionId);
    }

    [Fact]
    public void DriverLoadFailed_IsInfoWhenDeviceNowNormal_YellowWhenNot()
    {
        var health = new SystemHealth
        {
            Loaded = true,
            Events = [Event(SystemEvent.KernelPnpProvider, 219, Now.AddHours(-2), 3, "24", @"ACPI\INT3400\2&daba3ff&0", "3221226341", "14", @"\Driver\WUDFRd", "0")],
        };

        var normal = TestData.Device(@"ACPI\INT3400\2&daba3ff&0", "Intel Dynamic Tuning");
        var finding = Assert.Single(Diagnose(health, normal).Findings);
        Assert.Equal("driver-load-failed", finding.RuleId);
        Assert.Equal(FindingLevel.Info, finding.Level);
        Assert.Contains("WUDFRd", finding.Explanation);
        Assert.Contains("berfungsi normal", finding.Explanation);

        var broken = normal with { ErrorCode = 31 };
        var report = Diagnose(health, broken);
        Assert.Equal(FindingLevel.Warning, report.Findings.Single(f => f.RuleId == "driver-load-failed").Level);
    }

    [Fact]
    public void DriverCrash_LinksSuspectDriverToItsDevice()
    {
        var health = new SystemHealth
        {
            Loaded = true,
            Crashes = [new CrashReport { Time = Now.AddDays(-1), BugCheckCode = 0x9F, Parameters = [3, 0xffff], SuspectDriver = "Netwtw14.sys", DumpPath = @"C:\WINDOWS\Minidump\x.dmp", ReportId = "r1", StackDrivers = ["ntoskrnl.exe", "Netwtw14.sys"] }],
        };

        var finding = Assert.Single(Diagnose(health, Wifi).Findings);

        Assert.Equal("crash-driver", finding.RuleId);
        Assert.Equal(FindingLevel.Critical, finding.Level);
        Assert.Equal(Wifi.InstanceId, finding.Device?.InstanceId);
        Assert.Contains("sleep", finding.Explanation);
        Assert.Contains("Netwtw14.sys milik Intel Wi-Fi 6 AX200", finding.Explanation);
        Assert.Equal(["rollbackToSnapshot", "windowsUpdate", "copyHardwareId", "systemRestore"], finding.Actions.Select(a => a.ActionId));
        Assert.Contains(finding.Details, d => d.Key == "Kode blue screen" && d.Value.Contains("DRIVER_POWER_STATE_FAILURE"));
        Assert.Equal("crash-driver|r1", finding.Key);
        Assert.Equal(Now.AddDays(-1), finding.FirstSeen); // waktu blue screen, bukan waktu aplikasi melihatnya
    }

    [Fact]
    public void DriverCrash_WithoutSuspect_KeepsOnlyActionsThatNeedNoDevice()
    {
        var health = new SystemHealth
        {
            Loaded = true,
            Crashes = [new CrashReport { Time = Now.AddDays(-1), BugCheckCode = 0x157, DumpNote = "Jalankan sebagai Administrator untuk melihat driver yang dicurigai." }],
        };

        var finding = Assert.Single(Diagnose(health).Findings);

        Assert.Null(finding.Device);
        Assert.Equal([ActionCatalog.SystemRestore], finding.Actions.Select(a => a.ActionId));
        Assert.Contains("Administrator", finding.Explanation);
    }

    [Fact]
    public void HardwareCrash_0x12B_PointsToHardwareNotDriver()
    {
        var health = new SystemHealth
        {
            Loaded = true,
            Crashes = [new CrashReport { Time = Now.AddDays(-13), BugCheckCode = 0x12B, SuspectDriver = "Netwtw14.sys" }],
        };

        var finding = Assert.Single(Diagnose(health, Wifi).Findings);

        Assert.Equal("crash-hardware", finding.RuleId);
        Assert.Contains("RAM atau SSD", finding.Explanation);
        Assert.Equal([ActionCatalog.CheckHardware, ActionCatalog.CheckPower], finding.Actions.Select(a => a.ActionId));
    }

    [Fact]
    public void Crashes_OlderThan30Days_AreIgnored()
    {
        var health = new SystemHealth { Loaded = true, Crashes = [new CrashReport { Time = Now.AddDays(-31), BugCheckCode = 0x9F }] };

        Assert.Empty(Diagnose(health).Findings);
    }

    [Fact]
    public void Whea_OnlyErrorLevelCounts()
    {
        var health = new SystemHealth
        {
            Loaded = true,
            Events = [Event(SystemEvent.WheaProvider, 19, Now.AddDays(-1), level: 3), Event(SystemEvent.WheaProvider, 18, Now.AddDays(-2), level: 2)],
        };

        var finding = Assert.Single(Diagnose(health).Findings);
        Assert.Equal("whea", finding.RuleId);
        Assert.Contains("1 kali", finding.Explanation);
    }

    [Fact]
    public void UnexpectedShutdown_SaysWhetherABlueScreenCausedIt()
    {
        var withCrash = new SystemHealth { Loaded = true, Events = [Event(SystemEvent.KernelPowerProvider, 41, Now.AddDays(-1), 1, "159")] };
        var withoutCrash = new SystemHealth { Loaded = true, Events = [Event(SystemEvent.KernelPowerProvider, 41, Now.AddDays(-1), 1, "0")] };

        Assert.Contains("karena blue screen", Assert.Single(Diagnose(withCrash).Findings).Explanation);
        var plain = Assert.Single(Diagnose(withoutCrash).Findings);
        Assert.Contains("daya atau hardware", plain.Explanation);
        Assert.Equal(FindingLevel.Warning, plain.Level);
    }

    [Theory]
    [InlineData("0x00000157 (0xffffd385c1170080, 0x0000000000000005, 0x0000000000000002, 0x0000000000000000)", 0x157u, 4)]
    [InlineData("0x0000012b (0xffffffffc00002c4, 0x0000000000000cb3, 0x00000221e2106170, 0xffff8580db98e000)", 0x12Bu, 4)]
    [InlineData("0x0000009f (0x0000000000000003, 0xffffbf0d98725060, 0xfffff5040730f580, 0xffffbf0d9e1beb50)", 0x9Fu, 4)]
    public void BugCheckText_FromThisLaptopsEventLog_IsParsed(string text, uint code, int parameterCount)
    {
        var parsed = BugCheckCatalog.Parse(text);

        Assert.NotNull(parsed);
        Assert.Equal(code, parsed.Value.Code);
        Assert.Equal(parameterCount, parsed.Value.Parameters.Count);
    }

    [Theory]
    [InlineData(0x12Bu, CrashCause.Hardware)]
    [InlineData(0x124u, CrashCause.Hardware)]
    [InlineData(0x9Fu, CrashCause.Driver)]
    [InlineData(0x116u, CrashCause.Driver)]
    [InlineData(0xDEADu, CrashCause.Unknown)]
    public void BugCheckCatalog_KnowsCommonCauses(uint code, CrashCause cause) =>
        Assert.Equal(cause, BugCheckCatalog.Get(code).Cause);
}

public class MinidumpReaderTests
{
    private const int Stride = 0xA0;

    /// <summary>Membuat minidump triage 64-bit buatan sesuai format yang dibaca MinidumpReader.</summary>
    private static byte[] BuildDump(uint code, ulong[] parameters, (string Name, ulong Base, uint Size)[] drivers, ulong[] stack, ulong exceptionAddress = 0)
    {
        var data = new byte[0x8000];
        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x38), code);
        for (var i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x40 + i * 8), parameters[i]);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0xF10), exceptionAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xF98), 4); // triage

        const int listOffset = 0x2400, poolOffset = 0x4000, stackOffset = 0x6000;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2000 + 0x28), stackOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2000 + 0x2C), (uint)(stack.Length * 8));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2000 + 0x30), listOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2000 + 0x34), (uint)drivers.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2000 + 0x38), poolOffset);

        var pool = poolOffset;
        for (var i = 0; i < drivers.Length; i++)
        {
            var entry = listOffset + i * Stride;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(entry), (uint)pool);
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(entry + 8 + 0x30), drivers[i].Base);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(entry + 8 + 0x40), drivers[i].Size);
            var name = @"\SystemRoot\system32\drivers\" + drivers[i].Name;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(pool), (uint)name.Length);
            Encoding.Unicode.GetBytes(name).CopyTo(data, pool + 4);
            pool += 4 + name.Length * 2 + 2;
        }
        for (var i = 0; i < stack.Length; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(stackOffset + i * 8), stack[i]);
        }
        return data;
    }

    private static readonly (string, ulong, uint)[] Drivers =
    [
        ("ntoskrnl.exe", 0xFFFFF80000000000, 0x1000000),
        ("hal.dll", 0xFFFFF80001000000, 0x100000),
        ("wdf01000.sys", 0xFFFFF80002000000, 0x100000),
        ("Netwtw14.sys", 0xFFFFF80003000000, 0x400000),
        ("nvlddmkm.sys", 0xFFFFF80004000000, 0x2000000),
    ];

    [Fact]
    public void SuspectIsFirstNonCoreDriverOnTheStack()
    {
        var dump = BuildDump(0x9F, [3, 0x1234, 0x5678, 0x9ABC], Drivers,
            [0x1, 0xFFFFF80000001000, 0xFFFFF80002000100, 0xFFFFF80003000200, 0xFFFFF80004000300]);

        var info = MinidumpReader.Parse(dump);

        Assert.NotNull(info);
        Assert.Equal(0x9Fu, info.BugCheckCode);
        Assert.Equal(5, info.Drivers.Count);
        Assert.Equal("Netwtw14.sys", info.SuspectDriver);
        Assert.Equal(["ntoskrnl.exe", "wdf01000.sys", "Netwtw14.sys", "nvlddmkm.sys"], info.StackDrivers);
    }

    [Fact]
    public void ExceptionAddressWinsOverStack()
    {
        var dump = BuildDump(0x7E, [0xC0000005, 0, 0, 0], Drivers, [0xFFFFF80003000200], exceptionAddress: 0xFFFFF80004000010);

        Assert.Equal("nvlddmkm.sys", MinidumpReader.Parse(dump)?.SuspectDriver);
    }

    [Fact]
    public void OnlyCoreModules_NoSuspect()
    {
        var dump = BuildDump(0x157, [0, 0, 0, 0], Drivers, [0xFFFFF80000001000, 0xFFFFF80001000010]);

        var info = MinidumpReader.Parse(dump);
        Assert.Null(info?.SuspectDriver);
        Assert.Equal(["ntoskrnl.exe", "hal.dll"], info?.StackDrivers);
    }

    [Fact]
    public void GarbageDriverList_IsRejected_InsteadOfGuessing()
    {
        var dump = BuildDump(0x9F, [3, 0, 0, 0], Drivers, [0xFFFFF80003000200]);
        new Random(1).NextBytes(dump.AsSpan(0x2400, 0x600)); // rusak daftar driver

        var info = MinidumpReader.Parse(dump);

        Assert.NotNull(info);
        Assert.Equal(0x9Fu, info.BugCheckCode); // header tetap terbaca
        Assert.Empty(info.Drivers);
        Assert.Null(info.SuspectDriver);
    }

    [Fact]
    public void NotADump_ReturnsNull()
    {
        Assert.Null(MinidumpReader.Parse(new byte[0x3000]));
        Assert.Null(MinidumpReader.Parse(Encoding.ASCII.GetBytes("MDMP")));
    }

    /// <summary>Minidump asli di komputer ini (butuh Administrator; dilewati bila tidak bisa dibaca).</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void RealMinidumps_WhenReadable_ParseHeader()
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(@"C:\Windows\Minidump", "*.dmp");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return;
        }

        foreach (var file in files)
        {
            var info = MinidumpReader.Read(file);
            Assert.NotNull(info);
            Assert.NotEqual(0u, info.BugCheckCode);
        }
    }
}

public class EventLogIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemHealthReader_ReadsRealEventLog_WithoutThrowing()
    {
        var log = new MemoryLog();
        var health = await new SystemHealthReader(new EventLogService(log), log).ReadAsync(["Netwtw14", "nvlddmkm", null]);

        Assert.True(health.Loaded);
        Assert.All(health.Events, e => Assert.NotEqual(DateTime.MinValue, e.Time));
        Assert.All(health.Crashes, c => Assert.NotEqual(0u, c.BugCheckCode));
        // Tanpa hak Administrator, minidump tidak terbaca tetapi alasannya dicatat.
        Assert.All(health.Crashes.Where(c => !c.DumpRead && c.DumpPath is not null), c => Assert.NotNull(c.DumpNote));
    }
}

public class WindowsUpdateMatchTests
{
    private static DriverUpdate Update(string hwid, DateTime? date, string title = "Intel - Net - 22.250") =>
        new("id-" + hwid + date, title, hwid, "Intel", "Wi-Fi", "Net", date, 20_000_000);

    [Fact]
    public void MatchDevice_UsesHardwareAndCompatibleIds_AndSkipsOlderDrivers()
    {
        var device = TestData.Device("WIFI", hardwareIds: [@"PCI\VEN_8086&DEV_2723&SUBSYS_00848086"]) with
        {
            CompatibleIds = [@"PCI\VEN_8086&DEV_2723"],
            DriverDate = new DateTime(2023, 3, 5),
        };
        DriverUpdate[] updates =
        [
            Update(@"pci\ven_8086&dev_2723&subsys_00848086", new DateTime(2024, 6, 1), "baru"),
            Update(@"PCI\VEN_8086&DEV_2723", new DateTime(2025, 1, 1), "lebih baru"),
            Update(@"PCI\VEN_8086&DEV_2723", new DateTime(2022, 1, 1), "lama"),
            Update(@"PCI\VEN_10EC&DEV_8168", new DateTime(2025, 1, 1), "perangkat lain"),
        ];

        var matches = WindowsUpdateService.MatchDevice(updates, device);

        Assert.Equal(["lebih baru", "baru"], matches.Select(m => m.Title));
    }

    [Fact]
    public void MatchDevice_DeviceWithoutDriver_AcceptsAnyDate()
    {
        var device = TestData.Device("CAM", hardwareIds: ["USB\\VID_0BDA&PID_57FA"], inf: null, version: null) with { DriverDate = null };

        Assert.Single(WindowsUpdateService.MatchDevice([Update("USB\\VID_0BDA&PID_57FA", new DateTime(2019, 1, 1))], device));
    }

    [Theory]
    [InlineData(unchecked((int)0x8024402C), "internet")]
    [InlineData(unchecked((int)0x80070422), "dimatikan")]
    [InlineData(unchecked((int)0x8024002E), "WSUS")]
    [InlineData(unchecked((int)0x80004005), "0x80004005")]
    public void Describe_ExplainsCommonErrors(int hresult, string expected) =>
        Assert.Contains(expected, WindowsUpdateService.Describe(new System.Runtime.InteropServices.COMException("x", hresult)));
}
