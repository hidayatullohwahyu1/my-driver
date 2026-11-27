using Microsoft.Win32;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Logging;

namespace MyDriver.Windows.Health;

/// <summary>Mengumpulkan log kejadian, blue screen (dengan isi minidump), dan file driver tiap layanan.</summary>
public sealed class SystemHealthReader
{
    private const int EventDays = 30;
    private const int MaxDumpsRead = 5;

    private readonly EventLogService _events;
    private readonly ILogService _log;

    public SystemHealthReader(EventLogService events, ILogService log)
    {
        _events = events;
        _log = log;
    }

    public Task<SystemHealth> ReadAsync(IEnumerable<string?> serviceNames) =>
        Task.Run(() => Read(serviceNames.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList()));

    private SystemHealth Read(IReadOnlyList<string> serviceNames)
    {
        var events = _events.ReadRecent(EventDays);
        var crashes = events
            .Where(e => e.Is(SystemEvent.BugCheckProvider, 1001))
            .OrderByDescending(e => e.Time)
            .Select((e, index) => ToCrash(e, readDump: index < MaxDumpsRead))
            .OfType<CrashReport>()
            .ToList();

        var health = new SystemHealth
        {
            Events = events,
            Crashes = crashes,
            ServiceImages = ReadServiceImages(serviceNames),
            Loaded = true,
        };

        _log.Info($"Log kejadian {EventDays} hari: {events.Count} kejadian penting, {crashes.Count} blue screen" +
                  (crashes.Count > 0 ? $" (tersangka: {string.Join(", ", crashes.Select(c => c.SuspectDriver ?? "?"))})" : string.Empty) + ".");
        return health;
    }

    private CrashReport? ToCrash(SystemEvent e, bool readDump)
    {
        if (BugCheckCatalog.Parse(e.Property(0)) is not var (code, parameters))
        {
            return null;
        }

        var crash = new CrashReport
        {
            Time = e.Time,
            BugCheckCode = code,
            Parameters = parameters,
            DumpPath = e.Property(1) is { Length: > 0 } p ? p : null,
            ReportId = e.Property(2) is { Length: > 0 } r ? r : null,
        };
        if (!readDump || crash.DumpPath is null)
        {
            return crash;
        }

        try
        {
            if (!File.Exists(crash.DumpPath))
            {
                // File.Exists juga false bila folder tidak boleh dibaca.
                return crash with
                {
                    DumpNote = Directory.Exists(Path.GetDirectoryName(crash.DumpPath))
                        ? "File dump sudah tidak ada atau hanya bisa dibaca Administrator."
                        : "File dump sudah tidak ada.",
                };
            }

            var info = MinidumpReader.Read(crash.DumpPath);
            if (info is null)
            {
                return crash with { DumpNote = "Format file dump tidak dikenali." };
            }
            _log.Info($"Minidump {Path.GetFileName(crash.DumpPath)}: {info.Drivers.Count} driver terbaca, tersangka {info.SuspectDriver ?? "-"}, tumpukan {string.Join(" > ", info.StackDrivers)}");
            return crash with
            {
                DumpRead = true,
                SuspectDriver = info.SuspectDriver,
                StackDrivers = info.StackDrivers,
                DumpNote = info.Drivers.Count == 0 ? "Daftar driver di file dump tidak bisa dibaca." : null,
            };
        }
        catch (UnauthorizedAccessException)
        {
            return crash with { DumpNote = "Jalankan sebagai Administrator untuk melihat driver yang dicurigai." };
        }
        catch (IOException ex)
        {
            _log.Warning($"Minidump tidak bisa dibaca: {crash.DumpPath}", ex);
            return crash with { DumpNote = "File dump tidak bisa dibaca." };
        }
    }

    /// <summary>Nama layanan → nama file driver dari ImagePath di registry.</summary>
    private static Dictionary<string, string> ReadServiceImages(IEnumerable<string> services)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in services)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}");
                if (key?.GetValue("ImagePath") is string image && image.Length > 0)
                {
                    map[service] = Path.GetFileName(image.Trim('"'));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Lewati layanan yang tidak bisa dibaca.
            }
        }
        return map;
    }
}
