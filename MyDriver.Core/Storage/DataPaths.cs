namespace MyDriver.Core.Storage;

/// <summary>
/// Menentukan lokasi folder data portable <c>MyDriverData</c>.
/// Utamanya di samping file exe; bila tidak bisa ditulisi (misalnya di Program Files),
/// pindah ke <c>%LOCALAPPDATA%\MyDriver</c>.
/// </summary>
public sealed class DataPaths
{
    public const string DataFolderName = "MyDriverData";

    private DataPaths(string root, bool isFallback)
    {
        Root = root;
        IsFallback = isFallback;
    }

    /// <summary>Folder akar data.</summary>
    public string Root { get; }

    /// <summary>True bila folder di samping exe tidak bisa ditulisi sehingga memakai %LOCALAPPDATA%.</summary>
    public bool IsFallback { get; }

    public string LogsDirectory => Path.Combine(Root, "logs");
    public string ComputersDirectory => Path.Combine(Root, "computers");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string RulesFile => Path.Combine(Root, "rules.json");

    public static DataPaths Resolve(string exeDirectory, string? localAppDataDirectory = null)
    {
        var primary = Path.Combine(exeDirectory, DataFolderName);
        if (TryEnsureWritable(primary))
        {
            return new DataPaths(primary, isFallback: false);
        }

        var localAppData = localAppDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallback = Path.Combine(localAppData, "MyDriver");
        Directory.CreateDirectory(fallback);
        return new DataPaths(fallback, isFallback: true);
    }

    private static bool TryEnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".tulis-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
