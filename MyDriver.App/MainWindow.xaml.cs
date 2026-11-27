using System.Windows;

namespace MyDriver.App;

public partial class MainWindow : Window
{
    private const double PreferredWidth = 1120;
    private const double PreferredHeight = 720;
    private const double Gap = 24;

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
    }

    /// <summary>
    /// Menyesuaikan ukuran jendela dengan area layar di atas taskbar, agar tidak ada bagian
    /// yang tertutup di laptop 1366×768 atau saat skala tampilan 125–150%.
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;

        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Max(MinWidth, Math.Min(PreferredWidth, area.Width - Gap));
        Height = Math.Max(MinHeight, Math.Min(PreferredHeight, area.Height - Gap));
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;

        // Layar yang sangat kecil: langsung penuh.
        if (area.Width < MinWidth + Gap || area.Height < MinHeight + Gap)
        {
            WindowState = WindowState.Maximized;
        }
    }
}
