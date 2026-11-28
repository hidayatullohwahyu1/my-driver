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

    /// <summary>
    /// Nilai sebuah kunci di bagian [Version] (mis. "Class", "Provider"). Token %nama% diganti
    /// dengan isinya dari bagian [Strings].
    /// </summary>
    public static string? ParseVersionField(string infText, string key)
    {
        var version = Section(infText, "Version");
        var match = Regex.Match(version, @"^\s*" + Regex.Escape(key) + @"\s*=\s*(?<value>[^;\r\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var value = Unquote(match.Groups["value"].Value);
        var token = Regex.Match(value, "^%(?<name>[^%]+)%$");
        if (token.Success)
        {
            var strings = Section(infText, "Strings");
            var resolved = Regex.Match(strings, @"^\s*" + Regex.Escape(token.Groups["name"].Value) + @"\s*=\s*(?<value>[^;\r\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            value = resolved.Success ? Unquote(resolved.Groups["value"].Value) : value;
        }
        return value.Length == 0 ? null : value;
    }

    private static string Section(string text, string name)
    {
        var start = Regex.Match(text, @"^\s*\[" + Regex.Escape(name) + @"\]\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!start.Success)
        {
            return string.Empty;
        }
        var rest = text[(start.Index + start.Length)..];
        var next = Regex.Match(rest, @"^\s*\[[^\]]+\]\s*$", RegexOptions.Multiline);
        return next.Success ? rest[..next.Index] : rest;
    }

    private static string Unquote(string value) => value.Trim().Trim('"').Trim();

    public static string? ParseDriverVersion(string infText)
    {
        var match = DriverVerLine().Match(infText);
        return match.Success ? match.Groups["version"].Value.Trim() : null;
    }

    [GeneratedRegex(@"^\s*DriverVer\s*=\s*[^,\r\n]*,\s*(?<version>[0-9][0-9.]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DriverVerLine();
}
