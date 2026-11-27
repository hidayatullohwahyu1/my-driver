using System.Text;

namespace MyDriver.Core.Devices;

/// <summary>Identitas komputer: merek, model, BIOS, dan versi Windows.</summary>
public sealed record ComputerInfo
{
    public string? ComputerName { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SystemFamily { get; init; }
    public string? BiosVendor { get; init; }
    public string? BiosVersion { get; init; }
    public DateTime? BiosDate { get; init; }
    public string? SerialNumber { get; init; }

    /// <summary>UUID motherboard (Win32_ComputerSystemProduct.UUID), dasar ID komputer.</summary>
    public string? HardwareUuid { get; init; }
    public string? OsName { get; init; }

    /// <summary>Versi rilis Windows, mis. "22H2", "24H2".</summary>
    public string? OsDisplayVersion { get; init; }

    /// <summary>Nomor build lengkap, mis. "26100.2314".</summary>
    public string? OsBuild { get; init; }

    public string? OsArchitecture { get; init; }

    public static ComputerInfo Unknown { get; } = new();

    public string WindowsText =>
        string.Join(' ', new[] { OsName, OsDisplayVersion, OsBuild is null ? null : $"(build {OsBuild})", OsArchitecture }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string BiosText =>
        string.Join(' ', new[] { BiosVendor, BiosVersion, BiosDate is { } d ? $"({d:dd-MM-yyyy})" : null }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Teks untuk disalin lalu dicari di situs resmi produsen.</summary>
    public string ToClipboardText()
    {
        var sb = new StringBuilder();
        void Line(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sb.Append(label).Append(": ").AppendLine(value);
            }
        }

        Line("Merek", Manufacturer);
        Line("Model", Model);
        Line("Seri", SystemFamily);
        Line("Nomor seri", SerialNumber);
        Line("BIOS", BiosText);
        Line("Windows", WindowsText);
        return sb.ToString().TrimEnd();
    }
}
