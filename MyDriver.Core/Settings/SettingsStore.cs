using System.Text.Json;
using System.Text.Json.Serialization;
using MyDriver.Core.Logging;

namespace MyDriver.Core.Settings;

/// <summary>Membaca dan menyimpan <see cref="AppSettings"/> sebagai JSON.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _file;
    private readonly ILogService _log;

    public SettingsStore(string file, ILogService log)
    {
        _file = file;
        _log = log;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_file))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warning($"settings.json tidak bisa dibaca, memakai pengaturan bawaan: {_file}", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(_file, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Gagal menyimpan settings.json: {_file}", ex);
        }
    }
}
