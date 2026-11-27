using MyDriver.Core.Devices;
using MyDriver.Windows.Wmi;

namespace MyDriver.Tests;

public class ComputerInfoTests
{
    [Fact]
    public void ToClipboardText_ListsFilledFieldsOnly()
    {
        var info = new ComputerInfo
        {
            Manufacturer = "ASUSTeK COMPUTER INC.",
            Model = "X441UA",
            SerialNumber = "J1N0CV12345",
            BiosVendor = "American Megatrends Inc.",
            BiosVersion = "X441UA.308",
            BiosDate = new DateTime(2019, 3, 5),
            OsName = "Windows 11 Home",
            OsDisplayVersion = "24H2",
            OsBuild = "26100.2314",
            OsArchitecture = "64-bit",
        };

        Assert.Equal(
            "Merek: ASUSTeK COMPUTER INC.\r\n" +
            "Model: X441UA\r\n" +
            "Nomor seri: J1N0CV12345\r\n" +
            "BIOS: American Megatrends Inc. X441UA.308 (05-03-2019)\r\n" +
            "Windows: Windows 11 Home 24H2 (build 26100.2314) 64-bit",
            info.ToClipboardText().ReplaceLineEndings("\r\n"));
    }

    [Theory]
    [InlineData("To be filled by O.E.M.", null)]
    [InlineData("System Product Name", null)]
    [InlineData("Default string", null)]
    [InlineData("  ", null)]
    [InlineData(" Lenovo ", "Lenovo")]
    public void CleanOem_DropsFactoryPlaceholders(string value, string? expected) =>
        Assert.Equal(expected, WmiService.CleanOem(value));
}
