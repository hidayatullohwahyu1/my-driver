using System.Windows;
using Forms = System.Windows.Forms;

namespace MyDriver.App.Services;

/// <summary>
/// Ikon di area notifikasi (tray). Di Windows 10/11, balon dari ikon ini tampil sebagai notifikasi toast.
/// Dipakai saat aplikasi terbuka (dengan menu) dan saat scan latar (hanya notifikasi).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private System.Drawing.Icon? _iconImage;

    public TrayService(bool withMenu)
    {
        _icon = new Forms.NotifyIcon { Text = "My Driver!", Visible = true };
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/MyDriver.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                _iconImage = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
                _icon.Icon = _iconImage;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            _icon.Icon = System.Drawing.SystemIcons.Application;
        }

        _icon.BalloonTipClicked += (_, _) => NotificationClicked?.Invoke(this, EventArgs.Empty);
        _icon.BalloonTipClosed += (_, _) => NotificationClosed?.Invoke(this, EventArgs.Empty);

        if (withMenu)
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Buka My Driver!", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add("Scan sekarang", null, (_, _) => ScanRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Keluar", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? ScanRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? NotificationClicked;
    public event EventHandler? NotificationClosed;

    /// <summary>Teks saat kursor di atas ikon (maks. 63 karakter).</summary>
    public void SetStatus(string text) =>
        _icon.Text = text.Length <= 63 ? text : text[..60] + "...";

    public void ShowNotification(string title, string message, bool warning) =>
        _icon.ShowBalloonTip(15000, title, message, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage?.Dispose();
    }
}
