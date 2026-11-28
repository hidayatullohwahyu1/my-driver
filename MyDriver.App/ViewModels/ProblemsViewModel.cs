using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Repair;
using MyDriver.Core.Reports;

namespace MyDriver.App.ViewModels;

/// <summary>
/// Halaman Masalah: kartu masalah dengan rekomendasi bernomor. Setelah sebuah aksi, aplikasi
/// scan ulang; bila masalah hilang kartu ditandai selesai, bila belum langkah berikutnya disorot.
/// </summary>
public sealed partial class ProblemsViewModel : ObservableObject
{
    private readonly DiagnosisService _diagnosis;
    private readonly WorkspaceService _workspace;
    private readonly ScanCoordinator _scan;
    private readonly RepairService _repair;

    /// <summary>Kunci temuan → nomor langkah terakhir yang sudah dicoba.</summary>
    private readonly Dictionary<string, int> _tried = [];

    private CancellationTokenSource? _feedbackCts;

    public ProblemsViewModel(DiagnosisService diagnosis, WorkspaceService workspace, ScanCoordinator scan, RepairService repair)
    {
        _diagnosis = diagnosis;
        _workspace = workspace;
        _scan = scan;
        _repair = repair;
        _diagnosis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiagnosisService.Report))
            {
                Rebuild();
            }
        };
        _workspace.PropertyChanged += OnBusyChanged;
        _scan.PropertyChanged += OnBusyChanged;
        Rebuild();
    }

    public ObservableCollection<FindingCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    private bool _showInfo;

    [ObservableProperty]
    private string? _feedback;

    public bool HasReport => _diagnosis.Report is not null;

    public bool IsEmpty => HasReport && Cards.Count == 0;

    public bool IsBusy => _workspace.IsBusy || _scan.IsScanning;

    public string? BusyText => _workspace.BusyText ?? (_scan.IsScanning ? "Memeriksa ulang perangkat…" : null);

    public string SummaryText
    {
        get
        {
            if (_diagnosis.Report is not { } report)
            {
                return _scan.IsScanning ? "Sedang memindai…" : "Belum ada hasil diagnosis.";
            }
            var parts = new List<string>();
            if (report.CriticalCount > 0) parts.Add($"{report.CriticalCount} bermasalah");
            if (report.WarningCount > 0) parts.Add($"{report.WarningCount} perlu perhatian");
            var info = report.Findings.Count(f => f.Level == FindingLevel.Info);
            if (info > 0) parts.Add(ShowInfo ? $"{info} info" : $"{info} info disembunyikan");
            return parts.Count == 0 ? "Tidak ada masalah yang ditemukan." : string.Join(" · ", parts);
        }
    }

    public string EmptyText => ShowInfo || _diagnosis.Report?.Findings.Count is null or 0
        ? "Tidak ada masalah. Semua perangkat berfungsi normal."
        : "Tidak ada masalah yang perlu ditangani. Centang \"Tampilkan info\" untuk melihat catatan lain.";

    partial void OnShowInfoChanged(bool value) => Rebuild();

    [RelayCommand]
    private Task RescanAsync() => _scan.ScanAsync();

    /// <summary>Menyimpan laporan scan (HTML atau teks) untuk disimpan atau dikirim ke teknisi.</summary>
    [RelayCommand]
    private void ExportReport()
    {
        if (_scan.LastResult is not { } scan)
        {
            Dialogs.Info("Tunggu sampai scan perangkat selesai.");
            return;
        }

        var computer = _workspace.Store?.Profile.FolderName ?? "Komputer";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Simpan laporan My Driver!",
            FileName = $"Laporan-MyDriver-{computer}-{DateTime.Now:yyyy-MM-dd}",
            Filter = "Laporan HTML (*.html)|*.html|Teks biasa (*.txt)|*.txt",
            DefaultExt = ".html",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var input = new ReportInput(scan, _diagnosis.Report, DateTime.Now, AppEnvironment.Version,
            _workspace.LastBackup?.CreatedAt, _workspace.Snapshot?.CreatedAt);
        var html = !dialog.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        try
        {
            File.WriteAllText(dialog.FileName, html ? ReportBuilder.Html(input) : ReportBuilder.Text(input), new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"Laporan gagal disimpan: {ex.Message}");
            return;
        }

        if (Dialogs.Confirm($"Laporan disimpan:\n{dialog.FileName}\n\nBuka sekarang?"))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private async Task RunActionAsync(ActionItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        if (item.ActionId == ActionCatalog.CopyHardwareId)
        {
            var ids = item.Finding.Device?.HardwareIds ?? [];
            if (ids.Count > 0)
            {
                ShowFeedback(ClipboardHelper.TrySetText(string.Join(Environment.NewLine, ids))
                    ? "Hardware ID disalin. Tempelkan di situs resmi produsen untuk mencari driver."
                    : "Clipboard sedang dipakai aplikasi lain. Coba lagi.");
            }
            return;
        }

        var finding = item.Finding;
        var outcome = await _repair.ExecuteAsync(finding, item.ActionId);
        if (!outcome.Ran || item.ActionId is ActionCatalog.RestartWindows or ActionCatalog.SystemRestore)
        {
            return;
        }

        // Scan ulang sudah dijalankan; cek apakah temuan ini masih ada.
        var stillThere = _diagnosis.Report?.Findings.Any(f => f.Key == finding.Key) == true;
        if (!stillThere)
        {
            _tried.Remove(finding.Key);
            _workspace.AddHistory(new HistoryEntry
            {
                Time = DateTime.Now,
                Kind = HistoryKind.Resolved,
                Title = $"Masalah teratasi: {finding.Title}",
                Message = $"Teratasi setelah langkah {item.Number}: {item.Label}.",
                DeviceName = finding.Device?.Name,
                InstanceId = finding.Device?.InstanceId,
            });
            if (outcome.Success)
            {
                Dialogs.Info($"Masalah teratasi: {finding.Title}.");
            }
            return;
        }

        _tried[finding.Key] = Math.Max(item.Number, _tried.GetValueOrDefault(finding.Key));
        Rebuild();
        var next = item.Number + 1;
        ShowFeedback(outcome.RebootRequired
            ? "Perubahan baru berlaku setelah Windows di-restart."
            : next <= finding.Actions.Count
                ? $"Masalah belum teratasi. Coba langkah nomor {next}."
                : "Masalah belum teratasi dan semua langkah sudah dicoba. Simpan Hardware ID dan bawa ke teknisi bila perlu.");
    }

    private void Rebuild()
    {
        var expanded = Cards.Where(c => c.IsDetailsExpanded).Select(c => c.Finding.Key).ToHashSet();
        Cards.Clear();
        var report = _diagnosis.Report;
        foreach (var finding in report?.Findings ?? [])
        {
            if (finding.Level == FindingLevel.Info && !ShowInfo)
            {
                continue;
            }
            Cards.Add(new FindingCardViewModel(finding, _repair.CanModify, _tried.GetValueOrDefault(finding.Key))
            {
                IsDetailsExpanded = expanded.Contains(finding.Key),
            });
        }

        // Lupakan langkah yang pernah dicoba untuk temuan yang sudah hilang.
        foreach (var key in _tried.Keys.Where(k => report?.Findings.Any(f => f.Key == k) != true).ToList())
        {
            _tried.Remove(key);
        }

        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void OnBusyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(BusyText));
        OnPropertyChanged(nameof(SummaryText));
    }

    private async void ShowFeedback(string message)
    {
        Feedback = message;
        _feedbackCts?.Cancel();
        var cts = _feedbackCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(8000, cts.Token);
            Feedback = null;
        }
        catch (TaskCanceledException)
        {
        }
    }
}

public sealed partial class FindingCardViewModel : ObservableObject
{
    public FindingCardViewModel(Finding finding, bool isAdministrator, int lastTried)
    {
        Finding = finding;
        LastTried = lastTried;
        var highlight = Math.Min(lastTried + 1, Math.Max(1, finding.Actions.Count));
        Actions = finding.Actions
            .Select((a, i) => new ActionItemViewModel(finding, a.Info, i + 1, isAdministrator)
            {
                IsHighlighted = i + 1 == highlight,
                WasTried = i + 1 <= lastTried,
            })
            .ToList();

        var details = new List<KeyValuePair<string, string>>();
        if (finding.Device is { } d)
        {
            details.Add(new("Kode error", d.ErrorCode == 0 ? "0 (tidak ada)" : d.ErrorCode.ToString()));
            details.Add(new("Versi driver", d.DriverVersion ?? "–"));
            details.Add(new("File INF", d.InfName ?? "–"));
            details.Add(new("Kelas", d.PnpClass ?? "–"));
            details.Add(new("ID instans", d.InstanceId));
            details.Add(new("Hardware ID", d.HardwareIds.Count > 0 ? string.Join("\n", d.HardwareIds) : "–"));
            if (ActionPolicy.IsProtected(d))
            {
                details.Add(new("Terlindung", "Ya — tidak bisa di-uninstall, dinonaktifkan, atau di-restart dari aplikasi"));
            }
        }
        details.AddRange(finding.Details);
        details.Add(new("Aturan", finding.RuleId));
        TechnicalDetails = details;
    }

    public Finding Finding { get; }

    public int LastTried { get; }

    public IReadOnlyList<ActionItemViewModel> Actions { get; }

    public IReadOnlyList<KeyValuePair<string, string>> TechnicalDetails { get; }

    [ObservableProperty]
    private bool _isDetailsExpanded;

    public FindingLevel Level => Finding.Level;

    public string LevelText => Finding.Level switch
    {
        FindingLevel.Critical => "Bermasalah",
        FindingLevel.Warning => "Perhatian",
        _ => "Info",
    };

    public string Title => Finding.Title;

    public string Explanation => Finding.Explanation;

    public string? ChangeNote => Finding.ChangeNote;

    public string SinceText => $"Terlihat sejak {Format.DateTime(Finding.FirstSeen)} ({Format.Age(Finding.FirstSeen)})";

    public bool HasActions => Actions.Count > 0;

    public string? TriedText => LastTried > 0
        ? $"Langkah 1–{LastTried} sudah dicoba, masalah belum teratasi."
        : null;
}

public sealed partial class ActionItemViewModel : ObservableObject
{
    private readonly bool _isAdministrator;
    private readonly ActionCheck _check;

    public ActionItemViewModel(Finding finding, ActionInfo info, int number, bool isAdministrator)
    {
        Finding = finding;
        Info = info;
        Number = number;
        _isAdministrator = isAdministrator;
        _check = ActionPolicy.Check(info.Id, finding.Device);
    }

    public Finding Finding { get; }
    public ActionInfo Info { get; }
    public int Number { get; }

    /// <summary>Langkah yang disarankan sekarang (nomor 1, atau berikutnya setelah percobaan gagal).</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private bool _wasTried;

    public string ActionId => Info.Id;
    public string Label => Info.Label;
    public string Description => Info.Description;

    public bool HasButton => Info.Kind == ActionKind.Button;

    public bool IsAvailable => !RepairService.NotYetAvailable.Contains(Info.Id);

    public bool IsBlocked => !_check.IsAllowed;

    /// <summary>Tombol admin tetap aktif; saat ditekan tanpa admin, aplikasi menawarkan buka ulang sebagai Administrator.</summary>
    public bool CanRun => HasButton && IsAvailable && !IsBlocked;

    public string ButtonText => Info.Id switch
    {
        ActionCatalog.CopyHardwareId => "Salin",
        ActionCatalog.SystemRestore => "Buka",
        _ => "Jalankan",
    };

    public string? ButtonTooltip =>
        !IsAvailable ? "Pencarian lewat Windows Update menyusul pada tahap berikutnya."
        : IsBlocked ? _check.Reason
        : Info.RequiresAdministrator && !_isAdministrator ? "Butuh hak Administrator; saat ditekan, Anda ditawari membuka ulang aplikasi sebagai Administrator."
        : null;

    /// <summary>Peringatan singkat di bawah langkah (perangkat terlindung).</summary>
    public string? Note => _check.Permission switch
    {
        ActionPermission.Blocked => "Tidak tersedia untuk perangkat sistem penting.",
        ActionPermission.AllowedWithRedWarning => "Perangkat sistem penting: dengan peringatan dan konfirmasi ganda.",
        _ => null,
    };
}
