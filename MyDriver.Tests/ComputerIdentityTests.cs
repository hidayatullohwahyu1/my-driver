using MyDriver.Core.Storage;

namespace MyDriver.Tests;

public class ComputerIdentityTests
{
    [Fact]
    public void ComputeId_IsStableEightHexChars_AndIgnoresCaseAndSpaces()
    {
        var a = ComputerIdentity.ComputeId("4c4c4544-0035-3010-8052-b4c04f4e4c32", " j1n0cv12345 ");
        var b = ComputerIdentity.ComputeId("4C4C4544-0035-3010-8052-B4C04F4E4C32", "J1N0CV12345");

        Assert.Equal(a, b);
        Assert.Matches("^[0-9A-F]{8}$", a);
    }

    [Fact]
    public void ComputeId_DiffersWhenSerialDiffers() =>
        Assert.NotEqual(
            ComputerIdentity.ComputeId("4C4C4544-0035-3010-8052-B4C04F4E4C32", "AAA"),
            ComputerIdentity.ComputeId("4C4C4544-0035-3010-8052-B4C04F4E4C32", "BBB"));

    [Fact]
    public void ComputeId_IgnoresPlaceholderUuid()
    {
        Assert.Equal(
            ComputerIdentity.ComputeId("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", "SERIAL"),
            ComputerIdentity.ComputeId(null, "SERIAL"));
        Assert.Equal(
            ComputerIdentity.ComputeId("00000000-0000-0000-0000-000000000000", "SERIAL"),
            ComputerIdentity.ComputeId(null, "SERIAL"));
    }

    [Fact]
    public void ComputeId_FallsBackToComputerName_WhenNoHardwareIds() =>
        Assert.NotEqual(
            ComputerIdentity.ComputeId(null, null, "PC-A"),
            ComputerIdentity.ComputeId(null, null, "PC-B"));

    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", "X441UA", "ASUS_X441UA_8F3A2C1D")]
    [InlineData("HP", "HP Pavilion Laptop 14-dv0xxx", "HP_HP-Pavilion-Laptop-14-dv0xxx_8F3A2C1D")]
    [InlineData("LENOVO", "20L5S0H600", "Lenovo_20L5S0H600_8F3A2C1D")]
    [InlineData("Dell Inc.", "Latitude 5490", "Dell_Latitude-5490_8F3A2C1D")]
    [InlineData("Micro-Star International Co., Ltd.", "GF63 Thin", "MSI_GF63-Thin_8F3A2C1D")]
    [InlineData("Axioo", "MyBook 14", "Axioo_MyBook-14_8F3A2C1D")]
    [InlineData(null, null, "PC_Unknown_8F3A2C1D")]
    public void FolderName_UsesShortBrandAndSafeModel(string? manufacturer, string? model, string expected) =>
        Assert.Equal(expected, ComputerIdentity.FolderName(manufacturer, model, "8F3A2C1D"));
}
