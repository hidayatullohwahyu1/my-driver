using System.Diagnostics;
using MyDriver.Core.Logging;

namespace MyDriver.App.Services;

/// <summary>
/// Meneruskan galat binding WPF ke log aplikasi. Tanpa ini, tombol yang terhubung ke perintah yang
/// salah nama hanya "diam" tanpa pesan apa pun. Setiap pesan unik dicatat sekali.
/// </summary>
public sealed class BindingErrorListener : TraceListener
{
    private readonly ILogService _log;
    private readonly HashSet<string> _seen = [];
    private readonly object _gate = new();

    private BindingErrorListener(ILogService log)
    {
        _log = log;
    }

    public static void Attach(ILogService log)
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener(log));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
    }

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        lock (_gate)
        {
            if (_seen.Count > 200 || !_seen.Add(message))
            {
                return;
            }
        }
        _log.Warning($"[BINDING] {message}");
    }
}
