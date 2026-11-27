using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Logging;

namespace MyDriver.Windows.Health;

/// <summary>
/// Membaca kejadian penting dari log System: TDR kartu grafis (Display 4101), driver gagal dimuat
/// (Kernel-PnP 219), blue screen (BugCheck 1001), WHEA-Logger, dan mati mendadak (Kernel-Power 41).
/// Isi properti dibaca apa adanya sehingga tidak bergantung bahasa Windows.
/// </summary>
public sealed class EventLogService
{
    private const int MaxEvents = 500;

    private readonly ILogService _log;

    public EventLogService(ILogService log)
    {
        _log = log;
    }

    public IReadOnlyList<SystemEvent> ReadRecent(int days)
    {
        var milliseconds = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        var xpath =
            "*[System[(" +
            $"(Provider[@Name='{SystemEvent.DisplayProvider}'] and EventID=4101) or " +
            $"(Provider[@Name='{SystemEvent.KernelPnpProvider}'] and EventID=219) or " +
            $"(Provider[@Name='{SystemEvent.KernelPowerProvider}'] and EventID=41) or " +
            $"(Provider[@Name='{SystemEvent.BugCheckProvider}'] and EventID=1001) or " +
            $"Provider[@Name='{SystemEvent.WheaProvider}']" +
            $") and TimeCreated[timediff(@SystemTime) <= {milliseconds.ToString(CultureInfo.InvariantCulture)}]]]";

        var list = new List<SystemEvent>();
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true });
            for (var record = reader.ReadEvent(); record is not null && list.Count < MaxEvents; record = reader.ReadEvent())
            {
                using (record)
                {
                    list.Add(new SystemEvent(
                        record.TimeCreated ?? DateTime.MinValue,
                        record.ProviderName ?? string.Empty,
                        record.Id,
                        record.Level ?? 0,
                        record.Properties.Select(p => FormatValue(p.Value)).ToList()));
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            _log.Warning("Log System tidak bisa dibaca.", ex);
        }
        return list;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
