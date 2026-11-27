using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;

namespace MyDriver.App.ViewModels;

public enum HomeStatus
{
    Unknown,
    Safe,
    Attention,
    Problem,
}

public enum OnboardingStep
{
    None,
    AskHealthy,
    OfferBackup,
}

/// <summary>
/// Beranda: menjawab "apakah komputer saya aman?", dan memandu alur pertama kali
/// di komputer baru (snapshot sehat, lalu tawaran backup).
/// Penilaian Aman/Perhatian/Bermasalah datang dari mesin diagnosis (Tahap 4).
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly ScanCoordinator _scan;
    private readonly WorkspaceService _workspace;
    private readonly DriverActions _actions;
    private readonly DiagnosisService _diagnosis;

    public HomeViewModel(
        AppEnvironment environment, ScanCoordinator scan, WorkspaceService workspace,
        DriverActions actions, DiagnosisService diagnosis)
    {
        Environment = environment;
        _scan = scan;
        _workspace = workspace;
        _actions = actions;
        _diagnosis = diagnosis;
        _scan.PropertyChanged += OnStateChanged;
        _workspace.PropertyChanged += OnStateChanged;
        _diagnosis.PropertyChanged += OnStateChanged;
    }

    /// <summary>Diminta Beranda untuk membuka halaman Masalah.</summary>
    public event EventHandler? OpenProblemsRequested;

    public HomeStatus Status => _diagnosis.Report?.Status switch
    {
        OverallStatus.Safe => HomeStatus.Safe,
        OverallStatus.Attention => HomeStatus.Attention,
        OverallStatus.Problem => HomeStatus.Problem,
        _ => HomeStatus.Unknown,
    };

    public bool HasFindings => _diagnosis.Report is { } r && r.CriticalCount + r.WarningCount > 0;

    public string FindingCountText => _diagnosis.Report is { } r
        ? r.CriticalCount + r.WarningCount == 0
            ? "Tidak ada"
            : string.Join(", ", new[]
              {
                  r.CriticalCount > 0 ? $"{r.CriticalCount} merah" : null,
                  r.WarningCount > 0 ? $"{r.WarningCount} kuning" : null,
              }.Where(s => s is not null))
        : "–";

    public AppEnvironment Environment { get; }

    public bool IsScanning => _scan.IsScanning;

    public string StatusTitle => Status switch
    {
        HomeStatus.Safe => "Aman",
        HomeStatus.Attention => "Perlu perhatian",
        HomeStatus.Problem => "Bermasalah",
        _ => _scan.IsScanning ? "Sedang memindai…" : "Belum dipindai",
    };

    public string StatusDescription
    {
        get
        {
            if (_scan.LastError is { } error)
            {
                return error;
            }
            if (_diagnosis.Report is not { } report)
            {
                return "Aplikasi sedang membaca semua perangkat dan driver di komputer ini.";
            }

            var top = report.Findings.FirstOrDefault(f => f.Level != FindingLevel.Info);
            return report.Status switch
            {
                OverallStatus.Safe => "Semua perangkat berfungsi normal dan tidak ada masalah driver yang ditemukan.",
                OverallStatus.Problem when report.CriticalCount == 1 => $"{top!.Title}. Buka halaman Masalah untuk melihat solusinya.",
                OverallStatus.Problem => $"Ada {report.CriticalCount} masalah serius, misalnya: {top!.Title}. Buka halaman Masalah untuk melihat solusinya.",
                _ when report.WarningCount == 1 => $"{top!.Title}. Buka halaman Masalah untuk melihat sarannya.",
                _ => $"Ada {report.WarningCount} hal yang perlu diperhatikan. Buka halaman Masalah untuk melihat sarannya.",
            };
        }
    }

    public string LastScanText => _scan.LastResult is { } r ? $"{r.ScannedAt:HH.mm}, hari ini" : "Belum pernah";

    public string LastBackupText => _workspace.LastBackup is { } b
        ? $"{Format.Date(b.CreatedAt)} ({Format.Age(b.CreatedAt)})"
        : "Belum ada";

    public string DeviceCountText => _scan.LastResult is { } r
        ? ProblemCount(r) is var n and > 0 ? $"{r.ConnectedCount} ({n} error)" : r.ConnectedCount.ToString()
        : "–";

    // ---- Alur pertama kali ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskHealthy), nameof(IsOfferBackup), nameof(ShowOnboarding))]
    private OnboardingStep _step;

    [ObservableProperty]
    private string _offerBackupIntro = string.Empty;

    public bool ShowOnboarding => Step != OnboardingStep.None;

    public bool IsAskHealthy => Step == OnboardingStep.AskHealthy;

    public bool IsOfferBackup => Step == OnboardingStep.OfferBackup;

    public string ComputerName => _workspace.Store?.Profile is { } p ? $"{p.Manufacturer} {p.Model}".Trim() : "komputer ini";

    public string? ProblemNote => _scan.LastResult is { } r && ProblemCount(r) is var n and > 0
        ? $"Catatan: {n} perangkat sedang error atau dinonaktifkan dan tidak akan masuk snapshot."
        : null;

    public bool CanBackup => _actions.CanModify && !_workspace.IsBusy;

    public bool IsReadOnly => !_actions.CanModify;

    [RelayCommand]
    private void AnswerHealthy()
    {
        if (_scan.LastResult is { } scan)
        {
            _workspace.SaveSnapshot(scan);
            OfferBackupIntro = "Kondisi sehat sudah disimpan sebagai acuan.";
        }
        Step = OnboardingStep.OfferBackup;
    }

    [RelayCommand]
    private void AnswerNotHealthy()
    {
        OfferBackupIntro = "Snapshot belum dibuat. Setelah masalahnya beres, tandai kondisi sehat dari halaman Backup.";
        Step = OnboardingStep.OfferBackup;
    }

    [RelayCommand]
    private async Task BackupFromOnboardingAsync()
    {
        if (await _actions.BackupNowAsync())
        {
            FinishOnboarding();
        }
    }

    [RelayCommand]
    private void SkipBackup() => FinishOnboarding();

    [RelayCommand]
    private Task ScanAsync() => _scan.ScanAsync();

    [RelayCommand]
    private void OpenProblems() => OpenProblemsRequested?.Invoke(this, EventArgs.Empty);

    private void FinishOnboarding()
    {
        _workspace.CompleteOnboarding();
        Step = OnboardingStep.None;
    }

    private static int ProblemCount(ScanResult r) => r.ProblemCount + r.DisabledCount;

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_workspace.NeedsOnboarding && Step == OnboardingStep.None)
        {
            Step = OnboardingStep.AskHealthy;
        }

        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(FindingCountText));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(LastScanText));
        OnPropertyChanged(nameof(LastBackupText));
        OnPropertyChanged(nameof(DeviceCountText));
        OnPropertyChanged(nameof(ComputerName));
        OnPropertyChanged(nameof(ProblemNote));
        OnPropertyChanged(nameof(CanBackup));
    }
}
