using System.Text;

namespace MyDriver.Core.Logging;

/// <summary>
/// Menulis log ke satu file per hari: <c>logs/yyyy-MM-dd.log</c>.
/// Aman dipanggil dari banyak thread sekaligus.
/// </summary>
public sealed class FileLogService : ILogService
{
    private readonly string _logDirectory;
    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();

    public FileLogService(string logDirectory, Func<DateTime>? clock = null)
    {
        _logDirectory = logDirectory;
        _clock = clock ?? (() => DateTime.Now);
        Directory.CreateDirectory(_logDirectory);
    }

    public string CurrentLogFile => Path.Combine(_logDirectory, $"{_clock():yyyy-MM-dd}.log");

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        var now = _clock();
        var sb = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(LevelLabel(level)).Append("] ")
            .Append(message);
        if (exception is not null)
        {
            sb.AppendLine().Append(exception);
        }
        sb.AppendLine();

        var file = Path.Combine(_logDirectory, $"{now:yyyy-MM-dd}.log");
        lock (_gate)
        {
            try
            {
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
            catch (IOException)
            {
                // Log tidak boleh membuat aplikasi berhenti.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "PERINGATAN",
        LogLevel.Error => "GALAT",
        _ => level.ToString().ToUpperInvariant(),
    };
}
