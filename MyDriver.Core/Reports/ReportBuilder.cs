using System.Globalization;
using System.Net;
using System.Text;
using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;

namespace MyDriver.Core.Reports;

/// <summary>Data untuk satu laporan scan.</summary>
public sealed record ReportInput(
    ScanResult Scan,
    DiagnosisReport? Diagnosis,
    DateTime CreatedAt,
    string AppVersion,
    DateTime? LastBackupAt = null,
    DateTime? SnapshotAt = null);

/// <summary>
/// Laporan scan untuk disimpan atau dikirim ke teknisi: info komputer, status, temuan beserta
/// saran, dan daftar semua perangkat dan drivernya. Format HTML (satu file, tanpa file lain) atau teks.
/// </summary>
public static class ReportBuilder
{
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    public static string StatusText(OverallStatus? status) => status switch
    {
        OverallStatus.Safe => "Aman",
        OverallStatus.Attention => "Perlu perhatian",
        OverallStatus.Problem => "Bermasalah",
        _ => "Belum didiagnosis",
    };

    private static string LevelText(FindingLevel level) => level switch
    {
        FindingLevel.Critical => "Bermasalah",
        FindingLevel.Warning => "Perhatian",
        _ => "Info",
    };

    private static string StatusText(DeviceStatus status) => status switch
    {
        DeviceStatus.Normal => "Normal",
        DeviceStatus.Disabled => "Dinonaktifkan",
        DeviceStatus.NotConnected => "Tidak tersambung",
        _ => "Bermasalah",
    };

    private static string Date(DateTime? value) => value?.ToString("d MMMM yyyy, HH.mm", Indonesian) ?? "–";

    private static IEnumerable<DeviceInfo> OrderedDevices(ScanResult scan) =>
        scan.Devices
            .OrderBy(d => d.Status == DeviceStatus.Normal ? 1 : 0)
            .ThenBy(d => d.Category)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase);

    public static string Text(ReportInput input)
    {
        var c = input.Scan.Computer;
        var sb = new StringBuilder();
        sb.AppendLine("LAPORAN MY DRIVER!");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Dibuat        : {Date(input.CreatedAt)} (My Driver! {input.AppVersion})");
        sb.AppendLine($"Komputer      : {c.Manufacturer} {c.Model}".TrimEnd());
        sb.AppendLine($"Nomor seri    : {c.SerialNumber ?? "–"}");
        sb.AppendLine($"BIOS          : {c.BiosText}");
        sb.AppendLine($"Windows       : {c.WindowsText}");
        sb.AppendLine($"Scan          : {Date(input.Scan.ScannedAt)}, {input.Scan.ConnectedCount} perangkat tersambung");
        sb.AppendLine($"Backup terakhir: {Date(input.LastBackupAt)}");
        sb.AppendLine($"Snapshot sehat: {Date(input.SnapshotAt)}");
        sb.AppendLine($"STATUS        : {StatusText(input.Diagnosis?.Status)}");
        sb.AppendLine();

        var findings = input.Diagnosis?.Findings.Where(f => !f.HiddenByDefault).ToList() ?? [];
        sb.AppendLine($"TEMUAN ({findings.Count})");
        sb.AppendLine(new string('-', 60));
        if (findings.Count == 0)
        {
            sb.AppendLine("Tidak ada masalah yang ditemukan.");
        }
        foreach (var f in findings)
        {
            sb.AppendLine($"[{LevelText(f.Level)}] {f.Title}");
            sb.AppendLine($"  {f.Explanation}");
            if (f.ChangeNote is not null)
            {
                sb.AppendLine($"  {f.ChangeNote}");
            }
            for (var i = 0; i < f.Actions.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {f.Actions[i].Info.Label}");
            }
            if (f.Device is { } d)
            {
                sb.AppendLine($"  Perangkat: {d.Name} | Kode error {d.ErrorCode} | Driver {d.DriverVersion ?? "–"} | {d.InstanceId}");
            }
            foreach (var (key, value) in f.Details)
            {
                sb.AppendLine($"  {key}: {value.Replace("\n", ", ")}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("PERANGKAT");
        sb.AppendLine(new string('-', 60));
        foreach (var d in OrderedDevices(input.Scan))
        {
            sb.AppendLine($"{d.Category.DisplayName(),-12} {StatusText(d.Status),-16} {d.Name}");
            sb.AppendLine($"{"",-12} Driver {d.DriverVersion ?? "–"} ({d.DriverDate?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "–"}), {d.DriverProvider ?? "–"}, {d.InfName ?? "–"}");
            if (d.HardwareIds.Count > 0)
            {
                sb.AppendLine($"{"",-12} {d.HardwareIds[0]}");
            }
        }
        return sb.ToString();
    }

    public static string Html(ReportInput input)
    {
        static string E(string? text) => WebUtility.HtmlEncode(text ?? "–");

        var c = input.Scan.Computer;
        var status = input.Diagnosis?.Status;
        var statusClass = status switch
        {
            OverallStatus.Problem => "critical",
            OverallStatus.Attention => "warning",
            OverallStatus.Safe => "safe",
            _ => "info",
        };

        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="id"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Laporan My Driver!</title>
            <style>
            body{font-family:"Segoe UI",Arial,sans-serif;margin:32px auto;max-width:980px;color:#1b1f24;padding:0 16px;line-height:1.45}
            h1{margin:0 0 4px}h2{margin-top:32px;border-bottom:1px solid #e1e4ea;padding-bottom:6px}
            .muted{color:#5b6472}.grid{display:grid;grid-template-columns:180px 1fr;gap:4px 16px}
            .status{display:inline-block;padding:6px 14px;border-radius:999px;font-weight:600;color:#fff}
            .safe{background:#16a34a}.warning{background:#d97706}.critical{background:#dc2626}.info{background:#2563eb}
            .card{border:1px solid #e1e4ea;border-left-width:6px;border-radius:8px;padding:12px 16px;margin:12px 0}
            .card.critical{border-left-color:#dc2626;background:#fff}.card.warning{border-left-color:#d97706;background:#fff}.card.info{border-left-color:#2563eb;background:#fff}
            .level{font-size:12px;font-weight:600;text-transform:uppercase}.card.critical .level{color:#dc2626}.card.warning .level{color:#b45309}.card.info .level{color:#2563eb}
            table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #e1e4ea;padding:6px 8px;text-align:left;vertical-align:top}
            th{background:#f5f6f8}code{font-family:Consolas,monospace;font-size:12px}
            tr.bad td{background:#fef2f2}tr.off td{background:#fffbeb}
            </style></head><body>
            """);
        sb.Append($"<h1>Laporan My Driver!</h1><p class=\"muted\">Dibuat {E(Date(input.CreatedAt))} · My Driver! {E(input.AppVersion)}</p>");
        sb.Append($"<p><span class=\"status {statusClass}\">{E(StatusText(status))}</span></p>");

        sb.Append("<h2>Komputer</h2><div class=\"grid\">");
        void Row(string label, string? value) => sb.Append($"<div class=\"muted\">{E(label)}</div><div>{E(value)}</div>");
        Row("Merek dan model", $"{c.Manufacturer} {c.Model}".Trim());
        Row("Nomor seri", c.SerialNumber);
        Row("BIOS", c.BiosText);
        Row("Windows", c.WindowsText);
        Row("Scan", $"{Date(input.Scan.ScannedAt)} · {input.Scan.ConnectedCount} perangkat tersambung");
        Row("Backup terakhir", Date(input.LastBackupAt));
        Row("Snapshot sehat", Date(input.SnapshotAt));
        sb.Append("</div>");

        var findings = input.Diagnosis?.Findings.Where(f => !f.HiddenByDefault).ToList() ?? [];
        sb.Append($"<h2>Temuan ({findings.Count})</h2>");
        if (findings.Count == 0)
        {
            sb.Append("<p>Tidak ada masalah yang ditemukan.</p>");
        }
        foreach (var f in findings)
        {
            var cls = f.Level switch { FindingLevel.Critical => "critical", FindingLevel.Warning => "warning", _ => "info" };
            sb.Append($"<div class=\"card {cls}\"><div class=\"level\">{E(LevelText(f.Level))}</div><strong>{E(f.Title)}</strong><p>{E(f.Explanation)}</p>");
            if (f.ChangeNote is not null)
            {
                sb.Append($"<p><em>{E(f.ChangeNote)}</em></p>");
            }
            if (f.Actions.Count > 0)
            {
                sb.Append("<ol>");
                foreach (var a in f.Actions)
                {
                    sb.Append($"<li>{E(a.Info.Label)} <span class=\"muted\">— {E(a.Info.Description)}</span></li>");
                }
                sb.Append("</ol>");
            }
            var details = new List<KeyValuePair<string, string>>();
            if (f.Device is { } d)
            {
                details.Add(new("Perangkat", d.Name));
                details.Add(new("Kode error", d.ErrorCode.ToString(CultureInfo.InvariantCulture)));
                details.Add(new("Versi driver", d.DriverVersion ?? "–"));
                details.Add(new("ID instans", d.InstanceId));
                details.Add(new("Hardware ID", string.Join(", ", d.HardwareIds)));
            }
            details.AddRange(f.Details);
            if (details.Count > 0)
            {
                sb.Append("<div class=\"grid\">");
                foreach (var (key, value) in details)
                {
                    sb.Append($"<div class=\"muted\">{E(key)}</div><div><code>{E(value)}</code></div>");
                }
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        sb.Append($"<h2>Perangkat ({input.Scan.Devices.Count})</h2><table><tr><th>Kategori</th><th>Perangkat</th><th>Status</th><th>Driver</th><th>Penyedia</th><th>Hardware ID</th></tr>");
        foreach (var d in OrderedDevices(input.Scan))
        {
            var rowClass = d.Status switch { DeviceStatus.Problem => " class=\"bad\"", DeviceStatus.Disabled => " class=\"off\"", _ => string.Empty };
            var driver = $"{d.DriverVersion ?? "–"} ({d.DriverDate?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "–"})";
            sb.Append($"<tr{rowClass}><td>{E(d.Category.DisplayName())}</td><td>{E(d.Name)}</td><td>{E(StatusText(d.Status))}{(d.ErrorCode != 0 ? $" (Kode {d.ErrorCode})" : string.Empty)}</td>" +
                      $"<td>{E(driver)}<br><code>{E(d.InfName)}</code></td><td>{E(d.DriverProvider)}</td><td><code>{E(d.HardwareIds.FirstOrDefault())}</code></td></tr>");
        }
        sb.Append("</table><p class=\"muted\">Dibuat oleh My Driver!. Laporan ini hanya berisi informasi perangkat dan driver.</p></body></html>");
        return sb.ToString();
    }
}
