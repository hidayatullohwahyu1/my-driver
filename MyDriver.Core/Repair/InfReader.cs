using System.Text.RegularExpressions;

namespace MyDriver.Core.Repair;

/// <summary>Membaca baris <c>DriverVer</c> dari file INF (mis. "DriverVer = 03/05/2023,12.0.0.1272").</summary>
public static partial class InfReader
{
    /// <summary>Mengembalikan versi driver, atau null bila tidak ditemukan.</summary>
    public static string? ReadDriverVersion(string infPath)
    {
        try
        {
            // File INF bisa ANSI atau UTF-16 (dengan BOM); ReadAllText mengenali BOM.
            return ParseDriverVersion(File.ReadAllText(infPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? ParseDriverVersion(string infText)
    {
        var match = DriverVerLine().Match(infText);
        return match.Success ? match.Groups["version"].Value.Trim() : null;
    }

    [GeneratedRegex(@"^\s*DriverVer\s*=\s*[^,\r\n]*,\s*(?<version>[0-9][0-9.]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DriverVerLine();
}
