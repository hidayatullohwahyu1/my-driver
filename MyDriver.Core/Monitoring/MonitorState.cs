using MyDriver.Core.Diagnosis;

namespace MyDriver.Core.Monitoring;

/// <summary>Isi <c>monitor-state.json</c>: temuan yang sudah pernah diberitahukan, agar notifikasi tidak berulang.</summary>
public sealed record MonitorState
{
    public DateTime LastRun { get; init; }
    public OverallStatus LastStatus { get; init; } = OverallStatus.Safe;
    public IReadOnlyList<string> NotifiedKeys { get; init; } = [];
}

public sealed record MonitorDecision(bool Notify, string? Title, string? Message, MonitorState NewState, IReadOnlyList<Finding> NewFindings);

/// <summary>
/// Menentukan apakah scan otomatis perlu memunculkan notifikasi: hanya bila status Perhatian/Bermasalah
/// dan ada temuan kuning/merah yang belum pernah diberitahukan.
/// </summary>
public static class MonitorPolicy
{
    public static MonitorDecision Decide(DiagnosisReport report, MonitorState? previous, DateTime now)
    {
        var important = report.Findings.Where(f => f.Level != FindingLevel.Info).ToList();
        var known = new HashSet<string>(previous?.NotifiedKeys ?? [], StringComparer.Ordinal);
        var fresh = important.Where(f => !known.Contains(f.Key)).ToList();

        var state = new MonitorState
        {
            LastRun = now,
            LastStatus = report.Status,
            // Hanya temuan yang masih ada yang diingat: bila muncul lagi nanti, diberitahukan lagi.
            NotifiedKeys = important.Select(f => f.Key).ToList(),
        };

        if (report.Status == OverallStatus.Safe || fresh.Count == 0)
        {
            return new MonitorDecision(false, null, null, state, fresh);
        }

        var first = fresh.OrderByDescending(f => f.Level).First();
        var subject = first.Device?.Name ?? first.Title;
        var verb = first.Level == FindingLevel.Critical ? "bermasalah" : "perlu perhatian";
        var message = fresh.Count == 1
            ? (first.Device is null ? $"{first.Title}." : $"{subject} {verb}.") + " Klik untuk melihat solusi."
            : $"{fresh.Count} masalah baru ditemukan, misalnya: {subject}. Klik untuk melihat solusi.";
        return new MonitorDecision(true, "My Driver!", message, state, fresh);
    }
}
