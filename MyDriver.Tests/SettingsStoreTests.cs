using MyDriver.Core.Logging;
using MyDriver.Core.Settings;

namespace MyDriver.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsTheme()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "settings.json");
        var store = new SettingsStore(file, new MemoryLog());

        store.Save(new AppSettings { Theme = AppTheme.Dark });

        Assert.Equal(AppTheme.Dark, store.Load().Theme);
        Assert.Contains("\"theme\": \"dark\"", File.ReadAllText(file));
    }

    [Fact]
    public void Load_MissingOrCorruptFile_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "settings.json");
        var log = new MemoryLog();
        var store = new SettingsStore(file, log);

        Assert.Equal(AppTheme.System, store.Load().Theme);

        File.WriteAllText(file, "{ ini bukan json");
        Assert.Equal(AppTheme.System, store.Load().Theme);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
    }
}
