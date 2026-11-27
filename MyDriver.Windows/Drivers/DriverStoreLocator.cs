using System.Runtime.InteropServices;
using System.Text;

namespace MyDriver.Windows.Drivers;

/// <summary>Mencari folder paket driver di DriverStore dari nama INF terbit (mis. "oem12.inf").</summary>
public static class DriverStoreLocator
{
    /// <summary>
    /// "oem12.inf" → "netrtwlane.inf_amd64_1a2b3c4d" (nama folder di DriverStore\FileRepository,
    /// sama dengan nama subfolder hasil <c>dism /export-driver</c>). Null bila tidak ditemukan.
    /// </summary>
    public static string? GetPackageFolderName(string publishedInfName)
    {
        var path = GetInfPath(publishedInfName);
        return path is null ? null : Path.GetFileName(Path.GetDirectoryName(path));
    }

    public static string? GetInfPath(string publishedInfName)
    {
        var buffer = new StringBuilder(520);
        if (SetupGetInfDriverStoreLocationW(publishedInfName, IntPtr.Zero, null, buffer, buffer.Capacity, out var required))
        {
            return buffer.ToString();
        }

        if (required > buffer.Capacity)
        {
            buffer = new StringBuilder(required);
            if (SetupGetInfDriverStoreLocationW(publishedInfName, IntPtr.Zero, null, buffer, buffer.Capacity, out _))
            {
                return buffer.ToString();
            }
        }
        return null;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupGetInfDriverStoreLocationW(
        string fileName, IntPtr alternatePlatformInfo, string? localeName,
        StringBuilder returnBuffer, int returnBufferSize, out int requiredSize);
}
