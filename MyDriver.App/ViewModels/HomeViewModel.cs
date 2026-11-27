using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDriver.App.Services;
using MyDriver.Core.Devices;

namespace MyDriver.App.ViewModels;

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

    public HomeViewModel(AppEnvironment environment, ScanCoordinator scan, WorkspaceService workspace, DriverActions actions)
    {
        Environment = environment;
        _scan = scan;
        _workspace = workspace;
        _actions = actions;
        _scan.PropertyChanged += OnStateChanged;
        _workspace.PropertyChanged += OnStateChanged;
    }

    public AppEnvironment Environment { get; }

    public bool IsScanning => _scan.IsScanning;

    public string StatusTitle => _scan.LastResult is null
        ? (_scan.IsScanning ? "Sedang memindai…" : "Belum dipindai")
        : "Scan selesai";

    public string StatusDescription
    {
        get
        {
            if (_scan.LastError is { } error)
            {
                return error;
            }
            if (_scan.LastResult is not { } result)
            {
                return "Aplikasi sedang membaca semua perangkat dan driver di komputer ini.";
            }

            var problems = ProblemCount(result);
            var deviceText = problems == 0
                ? "Tidak ada perangkat yang melaporkan error."
                : $"{problems} perangkat melaporkan error. Lihat halaman Perangkat.";
            return $"{deviceText} Penilaian kesehatan lengkap beserta saran perbaikan menyusul setelah mesin diagnosis selesai.";
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
