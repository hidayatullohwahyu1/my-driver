using CommunityToolkit.Mvvm.ComponentModel;

namespace MyDriver.App.ViewModels;

/// <summary>
/// Dasar sementara untuk halaman yang isinya dikerjakan di tahap berikutnya.
/// Setiap halaman tetap punya ViewModel sendiri agar mudah diisi nanti.
/// </summary>
public abstract class PlaceholderPageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    public abstract string Glyph { get; }
    public abstract string PlannedContent { get; }
    public abstract string PlannedStage { get; }
}

public sealed class ProblemsViewModel : PlaceholderPageViewModel
{
    public override string Title => "Masalah";
    public override string Subtitle => "Daftar masalah driver beserta saran perbaikannya.";
    public override string Glyph => "";
    public override string PlannedContent =>
        "Kartu masalah berisi judul, penjelasan, sejak kapan terjadi, dan rekomendasi bernomor dengan tombol aksi.";
    public override string PlannedStage => "Tahap 4 dan 5";
}

public sealed class HistoryViewModel : PlaceholderPageViewModel
{
    public override string Title => "Riwayat";
    public override string Subtitle => "Catatan semua scan dan aksi perbaikan.";
    public override string Glyph => "";
    public override string PlannedContent =>
        "Semua scan dan aksi beserta hasilnya (berhasil/gagal) dan restore point yang dibuat.";
    public override string PlannedStage => "Tahap 5";
}
