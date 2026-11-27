namespace MyDriver.Core.Diagnosis;

/// <summary>Satu kejadian dari log System Windows (isi properti disimpan sebagai teks, tanpa bergantung bahasa).</summary>
public sealed record SystemEvent(DateTime Time, string Provider, int Id, int Level, IReadOnlyList<string> Properties)
{
    public const string DisplayProvider = "Display";
    public const string KernelPnpProvider = "Microsoft-Windows-Kernel-PnP";
    public const string KernelPowerProvider = "Microsoft-Windows-Kernel-Power";
    public const string WheaProvider = "Microsoft-Windows-WHEA-Logger";
    public const string BugCheckProvider = "Microsoft-Windows-WER-SystemErrorReporting";

    public string? Property(int index) => index < Properties.Count ? Properties[index] : null;

    public bool Is(string provider, int id) =>
        Id == id && string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Satu blue screen: dari event BugCheck 1001, dilengkapi isi minidump bila bisa dibaca.</summary>
public sealed record CrashReport
{
    public required DateTime Time { get; init; }
    public required uint BugCheckCode { get; init; }
    public IReadOnlyList<ulong> Parameters { get; init; } = [];
    public string? DumpPath { get; init; }
    public string? ReportId { get; init; }

    /// <summary>True bila file minidump berhasil dibaca.</summary>
    public bool DumpRead { get; init; }

    /// <summary>Alasan minidump tidak bisa dibaca (mis. butuh Administrator).</summary>
    public string? DumpNote { get; init; }

    /// <summary>Driver yang paling dicurigai (mis. "nvlddmkm.sys"), atau null.</summary>
    public string? SuspectDriver { get; init; }

    /// <summary>Driver yang terlihat di tumpukan saat crash, berurutan.</summary>
    public IReadOnlyList<string> StackDrivers { get; init; } = [];
}

/// <summary>Data kesehatan sistem di luar daftar perangkat: log kejadian, blue screen, file driver tiap layanan.</summary>
public sealed record SystemHealth
{
    public static SystemHealth Empty { get; } = new();

    public IReadOnlyList<SystemEvent> Events { get; init; } = [];
    public IReadOnlyList<CrashReport> Crashes { get; init; } = [];

    /// <summary>Nama layanan driver → nama file driver (mis. "Netwtw14" → "netwtw14.sys").</summary>
    public IReadOnlyDictionary<string, string> ServiceImages { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>True bila log kejadian sudah dibaca (bukan sekadar kosong karena belum dibaca).</summary>
    public bool Loaded { get; init; }
}
