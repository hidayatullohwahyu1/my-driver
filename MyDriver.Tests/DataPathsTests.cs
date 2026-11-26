using MyDriver.Core.Storage;

namespace MyDriver.Tests;

public class DataPathsTests
{
    [Fact]
    public void Resolve_WritableExeFolder_UsesMyDriverDataBesideExe()
    {
        using var temp = new TempDirectory();

        var paths = DataPaths.Resolve(temp.Path);

        Assert.False(paths.IsFallback);
        Assert.Equal(Path.Combine(temp.Path, "MyDriverData"), paths.Root);
        Assert.True(Directory.Exists(paths.Root));
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(paths.Root, "settings.json"), paths.SettingsFile);
        Assert.Empty(Directory.GetFiles(paths.Root)); // file uji tulis sudah dihapus
    }

    [Fact]
    public void Resolve_UnwritableExeFolder_FallsBackToLocalAppData()
    {
        using var temp = new TempDirectory();
        // Sebuah FILE bernama MyDriverData membuat folder dengan nama itu mustahil dibuat.
        var exeDir = Path.Combine(temp.Path, "exe");
        Directory.CreateDirectory(exeDir);
        File.WriteAllText(Path.Combine(exeDir, "MyDriverData"), "bukan folder");
        var localAppData = Path.Combine(temp.Path, "local");

        var paths = DataPaths.Resolve(exeDir, localAppData);

        Assert.True(paths.IsFallback);
        Assert.Equal(Path.Combine(localAppData, "MyDriver"), paths.Root);
        Assert.True(Directory.Exists(paths.Root));
    }
}
