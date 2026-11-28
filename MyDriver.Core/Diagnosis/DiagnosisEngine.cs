using System.Globalization;
using MyDriver.Core.Devices;
using MyDriver.Core.Snapshots;

namespace MyDriver.Core.Diagnosis;

public enum OverallStatus
{
    /// <summary>Tidak ada temuan kuning atau merah.</summary>
    Safe,

    /// <summary>Ada temuan kuning.</summary>
    Attention,

    /// <summary>Ada temuan merah.</summary>
    Problem,
}

/// <summary>Satu rekomendasi bernomor di kartu masalah.</summary>
public sealed record RecommendedAction(string ActionId)
{
    public ActionInfo Info => ActionCatalog.Get(ActionId);
}

/// <summary>Kartu masalah: hasil satu aturan untuk satu perangkat (atau untuk komputer).</summary>
public sealed record Finding
{
    /// <summary>Kunci unik temuan, dipakai untuk mencatat "sejak kapan".</summary>
    public required string Key { get; init; }
    public required string RuleId { get; init; }
    public required FindingLevel Level { get; init; }
    public required string Title { get; init; }
    public required string Explanation { get; init; }
    public IReadOnlyList<RecommendedAction> Actions { get; init; } = [];
    public DeviceInfo? Device { get; init; }
    public bool HiddenByDefault { get; init; }

    /// <summary>Catatan "driver berubah sejak snapshot sehat" (aturan perubahan + masalah).</summary>
    public string? ChangeNote { get; init; }

    /// <summary>Waktu temuan ini pertama kali terlihat.</summary>
    public DateTime FirstSeen { get; init; }

    /// <summary>Detail teknis tambahan (mis. kode blue screen, file dump, driver tersangka).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Details { get; init; } = [];
}

public sealed record DiagnosisInput(
    ScanResult Scan,
    HealthySnapshot? Snapshot,
    DateTime? LastBackupAt,
    DateTime Now,
    IReadOnlyDictionary<string, DateTime>? FirstSeen = null,
    SystemHealth? Health = null);

public sealed record DiagnosisReport(DateTime CreatedAt, IReadOnlyList<Finding> Findings)
{
    /// <summary>Bermasalah bila ada satu temuan merah; Perhatian bila ada temuan kuning; selain itu Aman.</summary>
    public OverallStatus Status =>
        Findings.Any(f => f.Level == FindingLevel.Critical) ? OverallStatus.Problem
        : Findings.Any(f => f.Level == FindingLevel.Warning) ? OverallStatus.Attention
        : OverallStatus.Safe;

    public int CriticalCount => Findings.Count(f => f.Level == FindingLevel.Critical);
    public int WarningCount => Findings.Count(f => f.Level == FindingLevel.Warning);
    public int VisibleInfoCount => Findings.Count(f => f.Level == FindingLevel.Info && !f.HiddenByDefault);

    /// <summary>Kunci temuan beserta waktu pertama terlihat, untuk disimpan.</summary>
    public IReadOnlyDictionary<string, DateTime> FirstSeenMap() =>
        Findings.ToDictionary(f => f.Key, f => f.FirstSeen);
}

/// <summary>
/// Mencocokkan hasil scan dan log kejadian dengan aturan, lalu menghasilkan kartu masalah.
/// Logika murni (tanpa akses Windows) agar setiap aturan bisa diuji dengan data contoh.
/// </summary>
public sealed class DiagnosisEngine
{
    private const int MaxCrashCards = 5;
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    private readonly RuleSet _rules;

    public DiagnosisEngine(RuleSet rules)
    {
        _rules = rules;
    }

    public DiagnosisReport Diagnose(DiagnosisInput input)
    {
        var findings = new List<Finding>();
        findings.AddRange(DeviceErrors(input));
        findings.AddRange(DriverConditions(input));
        findings.AddRange(BackupCondition(input));
        findings.AddRange(SnapshotChanges(input));
        if (input.Health is { } health)
        {
            var devices = new DeviceIndex(input.Scan.Devices, health.ServiceImages);
            findings.AddRange(GraphicsTimeouts(input, health, devices));
            findings.AddRange(DriverLoadFailures(input, health));
            findings.AddRange(Crashes(input, health, devices));
            findings.AddRange(HardwareErrors(input, health));
            findings.AddRange(UnexpectedShutdowns(input, health));
        }

        var ordered = findings
            .Select(f => f with
            {
                FirstSeen = f.FirstSeen != default ? f.FirstSeen
                    : input.FirstSeen?.GetValueOrDefault(f.Key) is { } seen && seen != default ? seen
                    : input.Now,
            })
            .OrderByDescending(f => f.Level)
            .ThenBy(f => f.HiddenByDefault)
            .ThenBy(f => f.Device?.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new DiagnosisReport(input.Now, ordered);
    }

    // ---- Perangkat ----

    private IEnumerable<Finding> DeviceErrors(DiagnosisInput input)
    {
        var rules = RulesOf(MatchTypes.DeviceError).ToList();
        var fallback = rules.FirstOrDefault(r => r.Match.Otherwise);

        foreach (var device in input.Scan.Devices)
        {
            var code = !device.IsPresent && device.ErrorCode == 0 ? DeviceInfo.ErrorNotConnected : device.ErrorCode;
            if (code == 0)
            {
                continue;
            }

            var rule = rules.FirstOrDefault(r => !r.Match.Otherwise && r.Match.MatchesCode(code)) ?? fallback;
            if (rule is null)
            {
                continue;
            }

            var finding = Create(rule, device, Values(device, ("code", code.ToString(CultureInfo.InvariantCulture))));
            yield return ApplyChangeRule(finding, device, input.Snapshot);
        }
    }

    private IEnumerable<Finding> DriverConditions(DiagnosisInput input)
    {
        var unsigned = RulesOf(MatchTypes.UnsignedDriver).FirstOrDefault();
        var old = RulesOf(MatchTypes.OldDriver).FirstOrDefault();

        foreach (var device in input.Scan.Devices.Where(d => d.IsPresent && d.HasDriver))
        {
            // Status tanda tangan baru diketahui setelah pemeriksaan latar selesai.
            if (unsigned is not null && input.Scan.SignaturesChecked && device.IsSigned == false)
            {
                yield return Create(unsigned, device, Values(device));
            }

            // Hanya driver pihak ketiga: driver bawaan Windows memakai tanggal lama (mis. 2006) secara sengaja.
            if (old is not null
                && device.Status == DeviceStatus.Normal
                && device.InfName?.StartsWith("oem", StringComparison.OrdinalIgnoreCase) == true
                && device.DriverDate is { } date
                && date.AddYears(old.Match.MinAgeYears ?? 5) < input.Now)
            {
                yield return Create(old, device, Values(device, ("age", AgeText(date, input.Now))));
            }
        }
    }

    private IEnumerable<Finding> BackupCondition(DiagnosisInput input)
    {
        if (input.LastBackupAt is not { } last)
        {
            if (RulesOf(MatchTypes.BackupMissing).FirstOrDefault() is { } missing)
            {
                yield return Create(missing, null, Values(null));
            }
            yield break;
        }

        if (RulesOf(MatchTypes.BackupOld).FirstOrDefault() is { } oldRule
            && (input.Now - last).TotalDays > (oldRule.Match.MaxAgeDays ?? 90))
        {
            yield return Create(oldRule, null, Values(null, ("age", AgeText(last, input.Now))));
        }
    }

    // ---- Perubahan dibanding snapshot sehat (F8) ----

    /// <summary>Hari tanpa masalah sebelum perubahan driver disarankan untuk ditandai sehat.</summary>
    public const int DaysBeforeSuggestingHealthy = 3;

    /// <summary>
    /// Driver berubah, driver baru, dan driver hilang dibanding snapshot sehat, untuk perangkat yang
    /// berfungsi normal. (Perangkat bermasalah ditangani aturan "perubahan + masalah".)
    /// </summary>
    private IEnumerable<Finding> SnapshotChanges(DiagnosisInput input)
    {
        if (input.Snapshot is not { } snapshot)
        {
            yield break;
        }

        var changed = RulesOf(MatchTypes.DriverChanged).FirstOrDefault();
        var added = RulesOf(MatchTypes.DriverNew).FirstOrDefault();
        var missing = RulesOf(MatchTypes.DriverMissing).FirstOrDefault();
        var snapshotDate = snapshot.CreatedAt.ToString("d MMMM yyyy", Indonesian);

        foreach (var device in input.Scan.Devices.Where(d => d.IsPresent && d.HasDriver && d.Status == DeviceStatus.Normal))
        {
            var healthy = FindInSnapshot(snapshot, device);
            if (healthy is null)
            {
                if (added is not null)
                {
                    yield return Create(added, device, Values(device, ("snapshotDate", snapshotDate)));
                }
                continue;
            }

            if (changed is null || string.Equals(healthy.DriverVersion, device.DriverVersion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var finding = Create(changed, device, Values(device,
                ("oldVersion", healthy.DriverVersion ?? "tidak diketahui"),
                ("newVersion", device.DriverVersion ?? "tidak diketahui"),
                ("snapshotDate", snapshotDate)));
            var days = input.FirstSeen?.GetValueOrDefault(finding.Key) is { } seen && seen != default
                ? (int)(input.Now - seen).TotalDays
                : 0;
            yield return finding with
            {
                Explanation = days >= DaysBeforeSuggestingHealthy
                    ? finding.Explanation + $" Sudah {days} hari tanpa masalah, jadi versi ini bisa ditandai sehat."
                    : finding.Explanation,
                Details = [new("Versi di snapshot", healthy.DriverVersion ?? "–"), new("Versi sekarang", device.DriverVersion ?? "–")],
            };
        }

        if (missing is null)
        {
            yield break;
        }
        foreach (var healthy in snapshot.Drivers)
        {
            var ids = new HashSet<string>(healthy.HardwareIds, StringComparer.OrdinalIgnoreCase);
            var stillThere = input.Scan.Devices.Any(d =>
                string.Equals(d.InstanceId, healthy.InstanceId, StringComparison.OrdinalIgnoreCase)
                || d.HardwareIds.Any(ids.Contains));
            if (stillThere)
            {
                continue;
            }

            var finding = Create(missing, null, Values(null, ("device", healthy.Name), ("snapshotDate", snapshotDate)));
            yield return finding with
            {
                Key = $"{missing.Id}|{healthy.InstanceId}",
                Details = [new("ID instans", healthy.InstanceId), new("Versi di snapshot", healthy.DriverVersion ?? "–")],
            };
        }
    }

    // ---- Log kejadian ----

    /// <summary>Kartu grafis berhenti merespons lalu pulih (Display 4101). Merah bila sering.</summary>
    private IEnumerable<Finding> GraphicsTimeouts(DiagnosisInput input, SystemHealth health, DeviceIndex devices)
    {
        if (RulesOf(MatchTypes.Tdr).FirstOrDefault() is not { } rule)
        {
            yield break;
        }

        var events = Within(health.Events, rule, input.Now, 7).Where(e => e.Is(SystemEvent.DisplayProvider, 4101));
        foreach (var group in events.GroupBy(e => e.Property(0) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var count = group.Count();
            var device = devices.ForDriver(group.Key, DeviceCategory.Display);
            var finding = Create(rule, device, Values(device,
                ("count", count.ToString(CultureInfo.InvariantCulture)),
                ("date", FormatDate(group.Max(e => e.Time)))));
            var level = count >= (rule.Match.CriticalCount ?? int.MaxValue) ? FindingLevel.Critical : rule.Level;
            yield return finding with
            {
                Key = $"{rule.Id}|{group.Key.ToLowerInvariant()}",
                Level = level,
                FirstSeen = group.Min(e => e.Time),
                Details = [new("Driver", group.Key), new("Jumlah kejadian", count.ToString(CultureInfo.InvariantCulture))],
            };
        }
    }

    /// <summary>Driver gagal dimuat saat start (Kernel-PnP 219).</summary>
    private IEnumerable<Finding> DriverLoadFailures(DiagnosisInput input, SystemHealth health)
    {
        if (RulesOf(MatchTypes.DriverLoadFailed).FirstOrDefault() is not { } rule)
        {
            yield break;
        }

        var events = Within(health.Events, rule, input.Now, 7).Where(e => e.Is(SystemEvent.KernelPnpProvider, 219));
        foreach (var group in events.GroupBy(e => e.Property(1) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key.Length == 0)
            {
                continue;
            }
            var latest = group.MaxBy(e => e.Time)!;
            var driver = (latest.Property(4) ?? string.Empty).Replace(@"\Driver\", string.Empty, StringComparison.OrdinalIgnoreCase);
            var device = input.Scan.Devices.FirstOrDefault(d => string.Equals(d.InstanceId, group.Key, StringComparison.OrdinalIgnoreCase));

            var finding = Create(rule, device, Values(device,
                ("driver", driver),
                ("count", group.Count().ToString(CultureInfo.InvariantCulture)),
                ("date", FormatDate(latest.Time))));
            var normalNow = device is { Status: DeviceStatus.Normal };
            yield return finding with
            {
                Key = $"{rule.Id}|{group.Key}",
                Level = rule.DowngradeWhenDeviceNormal && normalNow ? FindingLevel.Info : rule.Level,
                FirstSeen = group.Min(e => e.Time),
                Explanation = normalNow ? finding.Explanation + " Saat ini perangkat berfungsi normal." : finding.Explanation,
                Details = [new("Driver", driver), new("ID perangkat", group.Key), new("Kode status", latest.Property(2) ?? "–")],
            };
        }
    }

    /// <summary>Blue screen (BugCheck 1001 + minidump). Driver tersangka dicocokkan ke perangkat pemiliknya.</summary>
    private IEnumerable<Finding> Crashes(DiagnosisInput input, SystemHealth health, DeviceIndex devices)
    {
        var rules = RulesOf(MatchTypes.BugCheck).ToList();
        if (rules.Count == 0)
        {
            yield break;
        }

        var window = rules.Max(r => r.Match.WindowDays ?? 30);
        foreach (var crash in health.Crashes.Where(c => c.Time >= input.Now.AddDays(-window)).OrderByDescending(c => c.Time).Take(MaxCrashCards))
        {
            var info = BugCheckCatalog.Get(crash.BugCheckCode);
            var cause = info.Cause == CrashCause.Hardware ? "hardware" : "driver";
            var rule = rules.FirstOrDefault(r => string.Equals(r.Match.Cause, cause, StringComparison.OrdinalIgnoreCase))
                ?? rules.FirstOrDefault(r => r.Match.Cause is null);
            if (rule is null || crash.Time < input.Now.AddDays(-(rule.Match.WindowDays ?? 30)))
            {
                continue;
            }

            var device = crash.SuspectDriver is { } suspect ? devices.ForDriver(suspect) : null;
            var suspectText = crash.SuspectDriver is { } s
                ? device is null ? $"Driver yang dicurigai: {s}." : $"Driver yang dicurigai: {s} milik {device.Name}."
                : crash.DumpNote ?? "Driver penyebabnya tidak bisa dipastikan dari file dump.";

            var finding = Create(rule, device, Values(device,
                ("date", FormatDate(crash.Time)),
                ("meaning", info.Meaning),
                ("suspect", cause == "hardware" ? string.Empty : suspectText)));

            var details = new List<KeyValuePair<string, string>>
            {
                new("Kode blue screen", $"{BugCheckCatalog.Hex(crash.BugCheckCode)} {info.Name}"),
                new("Parameter", string.Join(", ", crash.Parameters.Select(p => "0x" + p.ToString("X", CultureInfo.InvariantCulture)))),
                new("File dump", crash.DumpPath ?? "–"),
                new("Driver tersangka", crash.SuspectDriver ?? "–"),
            };
            if (crash.StackDrivers.Count > 0)
            {
                details.Add(new("Driver di tumpukan", string.Join(", ", crash.StackDrivers)));
            }

            yield return finding with
            {
                Key = $"{rule.Id}|{crash.ReportId ?? crash.Time.ToString("O", CultureInfo.InvariantCulture)}",
                Explanation = finding.Explanation.Trim(),
                Details = details,
                FirstSeen = crash.Time,
            };
        }
    }

    /// <summary>Kesalahan hardware dari WHEA-Logger (tingkat galat/kritis).</summary>
    private IEnumerable<Finding> HardwareErrors(DiagnosisInput input, SystemHealth health)
    {
        if (RulesOf(MatchTypes.Whea).FirstOrDefault() is not { } rule)
        {
            yield break;
        }

        var events = Within(health.Events, rule, input.Now, 30)
            .Where(e => string.Equals(e.Provider, SystemEvent.WheaProvider, StringComparison.OrdinalIgnoreCase) && e.Level is 1 or 2)
            .ToList();
        if (events.Count == 0)
        {
            yield break;
        }

        var finding = Create(rule, null, Values(null,
            ("count", events.Count.ToString(CultureInfo.InvariantCulture)),
            ("date", FormatDate(events.Max(e => e.Time)))));
        yield return finding with
        {
            Details = [new("ID kejadian", string.Join(", ", events.Select(e => e.Id).Distinct()))],
            FirstSeen = events.Min(e => e.Time),
        };
    }

    /// <summary>Mati mendadak (Kernel-Power 41).</summary>
    private IEnumerable<Finding> UnexpectedShutdowns(DiagnosisInput input, SystemHealth health)
    {
        if (RulesOf(MatchTypes.UnexpectedShutdown).FirstOrDefault() is not { } rule)
        {
            yield break;
        }

        var events = Within(health.Events, rule, input.Now, 7).Where(e => e.Is(SystemEvent.KernelPowerProvider, 41)).ToList();
        if (events.Count == 0)
        {
            yield break;
        }

        // Properti 0 = kode BugCheck; bukan 0 berarti mati mendadak itu karena blue screen.
        var withCrash = events.Count(e => e.Property(0) is { } p && p != "0");
        var finding = Create(rule, null, Values(null,
            ("count", events.Count.ToString(CultureInfo.InvariantCulture)),
            ("date", FormatDate(events.Max(e => e.Time)))));
        yield return finding with
        {
            FirstSeen = events.Min(e => e.Time),
            Explanation = withCrash > 0
                ? finding.Explanation + $" {withCrash} di antaranya terjadi karena blue screen; lihat kartu blue screen."
                : finding.Explanation + " Tidak ada blue screen yang tercatat bersamaan, jadi penyebabnya kemungkinan daya atau hardware.",
        };
    }

    // ---- Aturan perubahan + masalah ----

    /// <summary>
    /// Bila perangkat bermasalah dan versi drivernya berbeda dari snapshot sehat,
    /// rekomendasi teratas selalu "Kembalikan ke versi snapshot".
    /// </summary>
    private static Finding ApplyChangeRule(Finding finding, DeviceInfo device, HealthySnapshot? snapshot)
    {
        if (snapshot is null || device.Status != DeviceStatus.Problem || device.ErrorCode == 14)
        {
            return finding;
        }

        var healthy = FindInSnapshot(snapshot, device);
        if (healthy is null || string.Equals(healthy.DriverVersion, device.DriverVersion, StringComparison.OrdinalIgnoreCase))
        {
            return finding;
        }

        var actions = finding.Actions.Where(a => a.ActionId != ActionCatalog.RollbackToSnapshot).ToList();
        actions.Insert(0, new RecommendedAction(ActionCatalog.RollbackToSnapshot));
        var now = device.DriverVersion is null ? "tidak ada driver" : $"versi {device.DriverVersion}";
        return finding with
        {
            Actions = actions,
            ChangeNote = $"Driver perangkat ini berubah sejak kondisi sehat dicatat pada {snapshot.CreatedAt.ToString("d MMMM yyyy", Indonesian)}: " +
                         $"versi {healthy.DriverVersion ?? "tidak diketahui"} menjadi {now}.",
        };
    }

    public static SnapshotDriver? FindInSnapshot(HealthySnapshot snapshot, DeviceInfo device)
    {
        var byInstance = snapshot.Drivers.FirstOrDefault(d =>
            string.Equals(d.InstanceId, device.InstanceId, StringComparison.OrdinalIgnoreCase));
        if (byInstance is not null)
        {
            return byInstance;
        }
        var ids = new HashSet<string>(device.HardwareIds, StringComparer.OrdinalIgnoreCase);
        return ids.Count == 0 ? null : snapshot.Drivers.FirstOrDefault(d => d.HardwareIds.Any(ids.Contains));
    }

    // ---- Pembantu ----

    private IEnumerable<DiagnosisRule> RulesOf(string type) => _rules.Rules.Where(r => r.Match.Type == type);

    private static IEnumerable<SystemEvent> Within(IEnumerable<SystemEvent> events, DiagnosisRule rule, DateTime now, int defaultDays) =>
        events.Where(e => e.Time >= now.AddDays(-(rule.Match.WindowDays ?? defaultDays)) && e.Time <= now.AddMinutes(5));

    private static Dictionary<string, string> Values(DeviceInfo? device, params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["device"] = device?.Name ?? "Perangkat" };
        foreach (var (key, value) in extra)
        {
            values[key] = value;
        }
        return values;
    }

    private static Finding Create(DiagnosisRule rule, DeviceInfo? device, IReadOnlyDictionary<string, string> values)
    {
        string Fill(string text)
        {
            foreach (var (key, value) in values)
            {
                text = text.Replace("{" + key + "}", value, StringComparison.Ordinal);
            }
            return text;
        }

        return new Finding
        {
            Key = device is null ? rule.Id : $"{rule.Id}|{device.InstanceId}",
            RuleId = rule.Id,
            Level = rule.Level,
            Title = Fill(rule.Title),
            Explanation = Fill(rule.Explanation),
            // Aksi khusus perangkat dibuang bila temuan ini tidak terkait perangkat tertentu.
            Actions = rule.Actions
                .Where(ActionCatalog.IsKnown)
                .Where(a => device is not null || !ActionCatalog.Get(a).NeedsDevice)
                .Select(a => new RecommendedAction(a))
                .ToList(),
            Device = device,
            HiddenByDefault = rule.HiddenByDefault,
        };
    }

    private static string FormatDate(DateTime date) => date.ToString("d MMMM yyyy, HH.mm", Indonesian);

    private static string AgeText(DateTime date, DateTime now)
    {
        var days = (now.Date - date.Date).TotalDays;
        return days >= 365
            ? $"{(int)(days / 365.25)} tahun lalu"
            : $"{(int)days} hari lalu";
    }

    /// <summary>Mencari perangkat pemilik sebuah file driver (.sys) atau nama layanan.</summary>
    private sealed class DeviceIndex(IReadOnlyList<DeviceInfo> devices, IReadOnlyDictionary<string, string> serviceImages)
    {
        public DeviceInfo? ForDriver(string driver, DeviceCategory? prefer = null)
        {
            if (string.IsNullOrWhiteSpace(driver))
            {
                return null;
            }

            var name = Path.GetFileName(driver.Trim());
            var baseName = Path.GetFileNameWithoutExtension(name);
            var matches = devices
                .Where(d => d.Service is { } svc
                    && (string.Equals(svc, baseName, StringComparison.OrdinalIgnoreCase)
                        || (serviceImages.TryGetValue(svc, out var image) && string.Equals(image, name, StringComparison.OrdinalIgnoreCase))
                        || (serviceImages.TryGetValue(svc, out image) && string.Equals(Path.GetFileNameWithoutExtension(image), baseName, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(d => d.Category == prefer)
                .ThenByDescending(d => d.IsPresent)
                .ThenByDescending(d => d.Status == DeviceStatus.Problem)
                .ToList();
            return matches.FirstOrDefault();
        }
    }
}
