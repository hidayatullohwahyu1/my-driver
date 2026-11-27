using MyDriver.Windows.Drivers;
using MyDriver.Windows.Wmi;

namespace MyDriver.Tests;

[Trait("Category", "Integration")]
public class DriverStoreLocatorIntegrationTests
{
    [Fact]
    public async Task GetPackageFolderName_FindsFileRepositoryFolder_ForInstalledOemInf()
    {
        var log = new MemoryLog();
        var scan = await new WmiDeviceScanner(new WmiService(log), log).ScanAsync();
        var oemInf = scan.Devices
            .Select(d => d.InfName)
            .FirstOrDefault(i => i?.StartsWith("oem", StringComparison.OrdinalIgnoreCase) == true);
        if (oemInf is null)
        {
            return; // komputer ini tidak punya driver pihak ketiga
        }

        var path = DriverStoreLocator.GetInfPath(oemInf!);
        var folder = DriverStoreLocator.GetPackageFolderName(oemInf!);

        Assert.NotNull(path);
        Assert.Contains(@"\DriverStore\FileRepository\", path, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"\.inf_amd64_[0-9a-f]+$", folder);
        Assert.Null(DriverStoreLocator.GetInfPath("oem99999.inf"));
    }
}
