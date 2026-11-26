using System.Diagnostics;
using System.Security.Principal;
using MyDriver.Core.Logging;

namespace MyDriver.Windows.Security;

/// <summary>Memeriksa hak Administrator dan menjalankan ulang aplikasi dengan hak tersebut.</summary>
public sealed class ElevationService
{
    private readonly ILogService _log;

    public ElevationService(ILogService log)
    {
        _log = log;
    }

    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Menjalankan ulang exe ini lewat dialog UAC.
    /// Mengembalikan false bila pengguna menolak atau gagal.
    /// </summary>
    public bool TryRestartAsAdministrator()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            });
            _log.Info("Aplikasi dijalankan ulang sebagai Administrator.");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 1223 = pengguna menekan "Tidak" di dialog UAC.
            _log.Warning($"Gagal menjalankan sebagai Administrator (kode {ex.NativeErrorCode}).");
            return false;
        }
    }
}
