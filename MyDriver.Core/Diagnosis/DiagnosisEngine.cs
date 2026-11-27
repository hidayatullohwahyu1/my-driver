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
}

public sealed record DiagnosisInput(
    ScanResult Scan,
    HealthySnapshot? Snapshot,
    DateTime? LastBackupAt,
    DateTime Now,
    IReadOnlyDictionary<string, DateTime>? FirstSeen = null);

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
/// Mencocokkan hasil scan dengan aturan dan menghasilkan kartu masalah.
/// Logika murni (tanpa akses Windows) agar setiap aturan bisa diuji dengan data contoh.
/// </summary>
public sealed class DiagnosisEngine
{
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

        var ordered = findings
            .Select(f => f with { FirstSeen = input.FirstSeen?.GetValueOrDefault(f.Key) is { } seen && seen != default ? seen : input.Now })
            .OrderByDescending(f => f.Level)
            .ThenBy(f => f.HiddenByDefault)
            .ThenBy(f => f.Device?.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new DiagnosisReport(input.Now, ordered);
    }

    private IEnumerable<Finding> DeviceErrors(DiagnosisInput input)
    {
        var rules = _rules.Rules.Where(r => r.Match.Type == MatchTypes.DeviceError).ToList();
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

            var finding = Create(rule, device, input, code: code);
            yield return ApplyChangeRule(finding, device, input.Snapshot);
        }
    }

    private IEnumerable<Finding> DriverConditions(DiagnosisInput input)
    {
        var unsigned = _rules.Rules.FirstOrDefault(r => r.Match.Type == MatchTypes.UnsignedDriver);
        var old = _rules.Rules.FirstOrDefault(r => r.Match.Type == MatchTypes.OldDriver);

        foreach (var device in input.Scan.Devices.Where(d => d.IsPresent && d.HasDriver))
        {
            // Status tanda tangan baru diketahui setelah pemeriksaan latar selesai.
            if (unsigned is not null && input.Scan.SignaturesChecked && device.IsSigned == false)
            {
                yield return Create(unsigned, device, input);
            }

            // Hanya driver pihak ketiga: driver bawaan Windows memakai tanggal lama (mis. 2006) secara sengaja.
            if (old is not null
                && device.Status == DeviceStatus.Normal
                && device.InfName?.StartsWith("oem", StringComparison.OrdinalIgnoreCase) == true
                && device.DriverDate is { } date
                && date.AddYears(old.Match.MinAgeYears ?? 5) < input.Now)
            {
                yield return Create(old, device, input, age: AgeText(date, input.Now));
            }
        }
    }

    private IEnumerable<Finding> BackupCondition(DiagnosisInput input)
    {
        if (input.LastBackupAt is not { } last)
        {
            var missing = _rules.Rules.FirstOrDefault(r => r.Match.Type == MatchTypes.BackupMissing);
            if (missing is not null)
            {
                yield return Create(missing, null, input);
            }
            yield break;
        }

        var oldRule = _rules.Rules.FirstOrDefault(r => r.Match.Type == MatchTypes.BackupOld);
        if (oldRule is not null && (input.Now - last).TotalDays > (oldRule.Match.MaxAgeDays ?? 90))
        {
            yield return Create(oldRule, null, input, age: AgeText(last, input.Now));
        }
    }

    /// <summary>
    /// Aturan "perubahan + masalah": bila perangkat bermasalah dan versi drivernya berbeda dari
    /// snapshot sehat, rekomendasi teratas selalu "Kembalikan ke versi snapshot".
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

    private static Finding Create(DiagnosisRule rule, DeviceInfo? device, DiagnosisInput input, int? code = null, string? age = null)
    {
        string Fill(string text) => text
            .Replace("{device}", device?.Name ?? "Perangkat", StringComparison.Ordinal)
            .Replace("{code}", code?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal)
            .Replace("{age}", age ?? string.Empty, StringComparison.Ordinal);

        return new Finding
        {
            Key = device is null ? rule.Id : $"{rule.Id}|{device.InstanceId}",
            RuleId = rule.Id,
            Level = rule.Level,
            Title = Fill(rule.Title),
            Explanation = Fill(rule.Explanation),
            Actions = rule.Actions.Where(ActionCatalog.IsKnown).Select(a => new RecommendedAction(a)).ToList(),
            Device = device,
            HiddenByDefault = rule.HiddenByDefault,
            FirstSeen = input.Now,
        };
    }

    private static string AgeText(DateTime date, DateTime now)
    {
        var days = (now.Date - date.Date).TotalDays;
        return days >= 365
            ? $"{(int)(days / 365.25)} tahun lalu"
            : $"{(int)days} hari lalu";
    }
}
