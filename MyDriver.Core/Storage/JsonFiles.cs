using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyDriver.Core.Storage;

/// <summary>Baca/tulis file JSON data aplikasi. Penulisan lewat file sementara agar tidak setengah jadi.</summary>
public static class JsonFiles
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Mengembalikan null bila file tidak ada. Melempar <see cref="JsonException"/> bila isinya rusak.</summary>
    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }
        File.Move(temp, path, overwrite: true);
    }
}
