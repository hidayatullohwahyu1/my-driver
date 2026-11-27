using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MyDriver.Core.Storage;

/// <summary>
/// ID komputer = hash dari UUID motherboard + nomor seri BIOS, sehingga tetap sama
/// walau Windows diinstal ulang. Nama folder: <c>merek_model_8-karakter-ID</c>.
/// </summary>
public static partial class ComputerIdentity
{
    private static readonly (string Contains, string Brand)[] Brands =
    [
        ("asus", "ASUS"),
        ("hewlett", "HP"),
        ("hp ", "HP"),
        ("lenovo", "Lenovo"),
        ("dell", "Dell"),
        ("acer", "Acer"),
        ("micro-star", "MSI"),
        ("msi", "MSI"),
        ("toshiba", "Toshiba"),
        ("dynabook", "Dynabook"),
        ("samsung", "Samsung"),
        ("microsoft", "Microsoft"),
        ("gigabyte", "Gigabyte"),
        ("huawei", "Huawei"),
        ("xiaomi", "Xiaomi"),
        ("fujitsu", "Fujitsu"),
        ("apple", "Apple"),
    ];

    /// <summary>8 karakter heksadesimal huruf besar, mis. "8F3A2C1D".</summary>
    public static string ComputeId(string? hardwareUuid, string? biosSerial, string? fallback = null)
    {
        var uuid = IsMeaningfulUuid(hardwareUuid) ? hardwareUuid!.Trim().ToUpperInvariant() : string.Empty;
        var serial = biosSerial?.Trim().ToUpperInvariant() ?? string.Empty;
        var basis = uuid.Length == 0 && serial.Length == 0
            ? $"nama|{fallback?.Trim().ToUpperInvariant()}"
            : $"{uuid}|{serial}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return Convert.ToHexString(hash, 0, 4);
    }

    public static string FolderName(string? manufacturer, string? model, string id) =>
        $"{Brand(manufacturer)}_{Sanitize(model, "Unknown")}_{id}";

    public static string Brand(string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            return "PC";
        }

        var lower = manufacturer.Trim().ToLowerInvariant() + " ";
        foreach (var (contains, brand) in Brands)
        {
            if (lower.Contains(contains, StringComparison.Ordinal))
            {
                return brand;
            }
        }
        return Sanitize(manufacturer.Trim().Split(' ')[0], "PC");
    }

    /// <summary>UUID "semua 0" atau "semua F" diisi pabrik dan tidak unik, jadi diabaikan.</summary>
    private static bool IsMeaningfulUuid(string? uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return false;
        }
        var hex = uuid.Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        return hex.Length > 0 && hex.Distinct().Count() > 1;
    }

    private static string Sanitize(string? value, string empty)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return empty;
        }
        var cleaned = UnsafeChars().Replace(value.Trim(), "-").Trim('-');
        if (cleaned.Length > 32)
        {
            cleaned = cleaned[..32].TrimEnd('-');
        }
        return cleaned.Length == 0 ? empty : cleaned;
    }

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex UnsafeChars();
}
