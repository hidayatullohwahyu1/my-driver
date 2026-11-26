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

public sealed class DevicesViewModel : PlaceholderPageViewModel
{
    public override string Title => "Perangkat";
    public override string Subtitle => "Semua perangkat di komputer ini, dikelompokkan per kategori.";
    public override string Glyph => "";
    public override string PlannedContent =>
        "Pohon perangkat per kategori (Jaringan, Suara, Layar, USB, Bluetooth, Penyimpanan, Lainnya), ikon status, detail driver, Hardware ID, dan info komputer.";
    public override string PlannedStage => "Tahap 2";
}

public sealed class BackupViewModel : PlaceholderPageViewModel
{
    public override string Title => "Backup";
    public override string Subtitle => "Cadangan driver dan snapshot kondisi sehat.";
    public override string Glyph => "";
    public override string PlannedContent =>
        "Tombol backup sekarang, daftar backup per tanggal, restore semua atau per perangkat, dan snapshot sehat.";
    public override string PlannedStage => "Tahap 3";
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
