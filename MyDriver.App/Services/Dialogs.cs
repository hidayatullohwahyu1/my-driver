using System.Globalization;
using System.Windows;

namespace MyDriver.App.Services;

/// <summary>Kotak dialog standar aplikasi (konfirmasi, info, galat).</summary>
public static class Dialogs
{
    private const string Title = "My Driver!";

    public static bool Confirm(string message, bool dangerous = false) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.YesNo,
            dangerous ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public static void Info(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warning(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Error(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.OK, MessageBoxImage.Error);

    private static Window Owner() => Application.Current.MainWindow;
}

/// <summary>Format angka dan tanggal untuk tampilan (Bahasa Indonesia).</summary>
public static class Format
{
    public static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{(bytes / (double)(1L << 30)).ToString("0.0", Indonesian)} GB",
        >= 1L << 20 => $"{(bytes / (double)(1L << 20)).ToString("0.0", Indonesian)} MB",
        >= 1L << 10 => $"{(bytes / (double)(1L << 10)).ToString("0", Indonesian)} KB",
        _ => $"{bytes} byte",
    };

    public static string DateTime(DateTime value) =>
        value.ToString("d MMMM yyyy, HH.mm", Indonesian);

    public static string Date(DateTime value) =>
        value.ToString("d MMMM yyyy", Indonesian);

    /// <summary>"hari ini", "kemarin", "5 hari lalu".</summary>
    public static string Age(DateTime value)
    {
        var days = (System.DateTime.Today - value.Date).Days;
        return days switch
        {
            <= 0 => "hari ini",
            1 => "kemarin",
            _ => $"{days} hari lalu",
        };
    }
}
