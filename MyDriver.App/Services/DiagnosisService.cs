using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Logging;

namespace MyDriver.App.Services;

/// <summary>
/// Menjalankan mesin diagnosis setiap kali hasil scan, snapshot, atau daftar backup berubah,
/// dan mencatat sejak kapan setiap temuan terlihat.
/// </summary>
public sealed partial class DiagnosisService : ObservableObject
{
    private readonly DiagnosisEngine _engine;
    private readonly ScanCoordinator _scan;
    private readonly WorkspaceService _workspace;
    private readonly ILogService _log;
    private string? _lastSummary;

    public DiagnosisService(RuleSet rules, ScanCoordinator scan, WorkspaceService workspace, ILogService log)
    {
        _engine = new DiagnosisEngine(rules);
        _scan = scan;
        _workspace = workspace;
        _log = log;
        _scan.PropertyChanged += OnChanged;
        _workspace.PropertyChanged += OnChanged;
    }

    [ObservableProperty]
    private DiagnosisReport? _report;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScanCoordinator.LastResult) or nameof(ScanCoordinator.SystemHealth) or nameof(WorkspaceService.Store)
            or nameof(WorkspaceService.Snapshot) or nameof(WorkspaceService.Backups))
        {
            Run();
        }
    }

    private void Run()
    {
        if (_scan.LastResult is not { } scan || _workspace.Store is not { } store)
        {
            return;
        }

        var report = _engine.Diagnose(new DiagnosisInput(
            scan, _workspace.Snapshot, _workspace.LastBackup?.CreatedAt, DateTime.Now, store.LoadFindingsSeen(), _scan.SystemHealth));

        try
        {
            // Sebelum log kejadian terbaca, temuan blue screen/TDR belum ada; jangan sampai
            // catatan "sejak kapan"-nya terhapus.
            if (_scan.SystemHealth is not null)
            {
                store.SaveFindingsSeen(report.FirstSeenMap());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("findings-seen.json tidak bisa disimpan.", ex);
        }

        var summary = $"Diagnosis: {report.Status}, {report.CriticalCount} merah, {report.WarningCount} kuning, {report.VisibleInfoCount} info.";
        if (summary != _lastSummary)
        {
            _log.Info(summary);
            foreach (var finding in report.Findings.Where(f => f.Level != FindingLevel.Info))
            {
                _log.Info($"  [{finding.Level}] {finding.RuleId}: {finding.Title}");
            }
            _lastSummary = summary;
        }

        Report = report;
    }
}
