using System.Reflection;
using System.Text.Json;
using MyDriver.Core.Logging;
using MyDriver.Core.Storage;

namespace MyDriver.Core.Diagnosis;

public enum FindingLevel
{
    Info,
    Warning,
    Critical,
}

/// <summary>Jenis temuan yang dicocokkan aturan.</summary>
public static class MatchTypes
{
    public const string DeviceError = "deviceError";
    public const string UnsignedDriver = "unsignedDriver";
    public const string OldDriver = "oldDriver";
    public const string BackupMissing = "backupMissing";
    public const string BackupOld = "backupOld";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        DeviceError, UnsignedDriver, OldDriver, BackupMissing, BackupOld,
    };
}

public sealed record RuleMatch
{
    public required string Type { get; init; }

    /// <summary>deviceError: satu kode.</summary>
    public int? Code { get; init; }

    /// <summary>deviceError: beberapa kode.</summary>
    public IReadOnlyList<int>? Codes { get; init; }

    /// <summary>deviceError: berlaku untuk kode yang tidak dikenali aturan lain.</summary>
    public bool Otherwise { get; init; }

    /// <summary>oldDriver: umur minimal driver (tahun).</summary>
    public int? MinAgeYears { get; init; }

    /// <summary>backupOld: umur maksimal backup (hari).</summary>
    public int? MaxAgeDays { get; init; }

    public bool MatchesCode(int code) => Code == code || Codes?.Contains(code) == true;
}

/// <summary>Satu aturan di <c>rules.json</c>.</summary>
public sealed record DiagnosisRule
{
    public required string Id { get; init; }
    public required RuleMatch Match { get; init; }
    public required FindingLevel Level { get; init; }

    /// <summary>Judul tanpa istilah teknis. Penanda: {device}, {code}, {age}.</summary>
    public required string Title { get; init; }

    public required string Explanation { get; init; }

    /// <summary>Rekomendasi berurutan dari yang paling aman.</summary>
    public IReadOnlyList<string> Actions { get; init; } = [];

    /// <summary>Temuan disembunyikan secara bawaan (mis. perangkat tidak tersambung).</summary>
    public bool HiddenByDefault { get; init; }
}

/// <summary>Isi <c>rules.json</c>.</summary>
public sealed record RuleSet
{
    public const string FileName = "rules.json";
    private const string ResourceName = "MyDriver.Core.rules.json";

    public int Version { get; init; }
    public IReadOnlyList<DiagnosisRule> Rules { get; init; } = [];

    /// <summary>Aturan bawaan yang ikut di dalam exe.</summary>
    public static RuleSet LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Resource {ResourceName} tidak ditemukan.");
        return JsonSerializer.Deserialize<RuleSet>(stream, JsonFiles.Options)
            ?? throw new InvalidOperationException("rules.json bawaan kosong.");
    }

    public static string EmbeddedJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Memakai <c>MyDriverData/rules.json</c> bila ada, valid, dan versinya tidak lebih lama dari bawaan;
    /// selain itu memakai aturan bawaan (dan menulis ulang file bila hilang atau lebih lama).
    /// </summary>
    public static RuleSet Load(string file, ILogService log)
    {
        var embedded = LoadEmbedded();
        RuleSet? fromFile = null;
        try
        {
            fromFile = JsonFiles.Read<RuleSet>(file);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warning($"rules.json tidak bisa dibaca, memakai aturan bawaan: {file}", ex);
        }

        if (fromFile is not null)
        {
            var errors = fromFile.Validate();
            if (errors.Count > 0)
            {
                log.Warning($"rules.json tidak valid, memakai aturan bawaan:\n  {string.Join("\n  ", errors)}");
                return embedded;
            }
            if (fromFile.Version >= embedded.Version)
            {
                log.Info($"Aturan dari {file} (versi {fromFile.Version}, {fromFile.Rules.Count} aturan).");
                return fromFile;
            }
            log.Info($"rules.json versi {fromFile.Version} lebih lama dari bawaan versi {embedded.Version}; diganti.");
        }

        TryWriteEmbedded(file, log);
        return embedded;
    }

    /// <summary>Daftar kesalahan isi aturan (kosong bila valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var group in Rules.GroupBy(r => r.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"ID aturan ganda: {group.Key}");
        }
        foreach (var rule in Rules)
        {
            if (!MatchTypes.All.Contains(rule.Match.Type))
            {
                errors.Add($"{rule.Id}: jenis match tidak dikenal '{rule.Match.Type}'");
            }
            if (rule.Match.Type == MatchTypes.DeviceError && rule.Match.Code is null && rule.Match.Codes is not { Count: > 0 } && !rule.Match.Otherwise)
            {
                errors.Add($"{rule.Id}: deviceError butuh code, codes, atau otherwise");
            }
            foreach (var action in rule.Actions.Where(a => !ActionCatalog.IsKnown(a)))
            {
                errors.Add($"{rule.Id}: aksi tidak dikenal '{action}'");
            }
        }
        return errors;
    }

    private static void TryWriteEmbedded(string file, ILogService log)
    {
        try
        {
            File.WriteAllText(file, EmbeddedJson());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning($"rules.json bawaan tidak bisa ditulis ke {file}", ex);
        }
    }
}
