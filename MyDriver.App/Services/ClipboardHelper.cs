using System.Runtime.InteropServices;
using System.Windows;

namespace MyDriver.App.Services;

public static class ClipboardHelper
{
    /// <summary>
    /// Menyalin teks ke clipboard. Clipboard kadang sedang dipakai aplikasi lain,
    /// jadi dicoba beberapa kali sebelum menyerah.
    /// </summary>
    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(50);
            }
        }
        return false;
    }
}
