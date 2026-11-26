using CommunityToolkit.Mvvm.ComponentModel;
using MyDriver.App.Services;

namespace MyDriver.App.ViewModels;

/// <summary>
/// Beranda: menjawab "apakah komputer saya aman?".
/// Pada Tahap 1 belum ada scan, jadi status masih "Belum dipindai".
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    public HomeViewModel(AppEnvironment environment)
    {
        Environment = environment;
    }

    public AppEnvironment Environment { get; }

    public string StatusTitle => "Belum dipindai";

    public string StatusDescription =>
        "Fitur scan perangkat akan tersedia pada tahap berikutnya. Setelah itu, halaman ini menampilkan apakah semua driver aman.";

    public string LastScanText => "Belum pernah";

    public string LastBackupText => "Belum ada";

    public string FindingCountText => "–";
}
