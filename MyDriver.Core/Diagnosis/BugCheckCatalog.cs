using System.Globalization;

namespace MyDriver.Core.Diagnosis;

public enum CrashCause
{
    /// <summary>Biasanya disebabkan driver.</summary>
    Driver,

    /// <summary>Biasanya disebabkan hardware (RAM, SSD, CPU, suhu, daya).</summary>
    Hardware,

    Unknown,
}

public sealed record BugCheckInfo(uint Code, string Name, string Meaning, CrashCause Cause);

/// <summary>Arti kode blue screen yang paling sering, dalam bahasa sederhana.</summary>
public static class BugCheckCatalog
{
    private static readonly Dictionary<uint, BugCheckInfo> Known = new BugCheckInfo[]
    {
        new(0x0A, "IRQL_NOT_LESS_OR_EQUAL", "Sebuah driver mengakses memori yang tidak boleh diaksesnya.", CrashCause.Driver),
        new(0x19, "BAD_POOL_HEADER", "Data di memori sistem rusak, biasanya oleh driver.", CrashCause.Driver),
        new(0x1A, "MEMORY_MANAGEMENT", "Terjadi kesalahan pengelolaan memori. Penyebabnya bisa driver atau RAM yang rusak.", CrashCause.Unknown),
        new(0x1E, "KMODE_EXCEPTION_NOT_HANDLED", "Sebuah driver mengalami kesalahan yang tidak tertangani.", CrashCause.Driver),
        new(0x3B, "SYSTEM_SERVICE_EXCEPTION", "Sebuah driver atau layanan sistem mengalami kesalahan.", CrashCause.Driver),
        new(0x50, "PAGE_FAULT_IN_NONPAGED_AREA", "Driver membaca memori yang tidak ada. Penyebabnya bisa driver atau RAM.", CrashCause.Unknown),
        new(0x7A, "KERNEL_DATA_INPAGE_ERROR", "Windows gagal membaca data dari disk. Periksa kondisi SSD/HDD dan kabelnya.", CrashCause.Hardware),
        new(0x7B, "INACCESSIBLE_BOOT_DEVICE", "Windows tidak bisa mengakses disk sistem saat menyala.", CrashCause.Unknown),
        new(0x7E, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Sebuah driver mengalami kesalahan yang tidak tertangani.", CrashCause.Driver),
        new(0x9F, "DRIVER_POWER_STATE_FAILURE", "Sebuah driver gagal saat komputer masuk atau bangun dari sleep/hibernate.", CrashCause.Driver),
        new(0xA0, "INTERNAL_POWER_ERROR", "Terjadi kesalahan pengelolaan daya.", CrashCause.Unknown),
        new(0xC2, "BAD_POOL_CALLER", "Sebuah driver memakai memori sistem dengan cara yang salah.", CrashCause.Driver),
        new(0xC4, "DRIVER_VERIFIER_DETECTED_VIOLATION", "Driver Verifier menemukan driver yang melanggar aturan.", CrashCause.Driver),
        new(0xD1, "DRIVER_IRQL_NOT_LESS_OR_EQUAL", "Sebuah driver mengakses memori yang tidak boleh diaksesnya.", CrashCause.Driver),
        new(0xEF, "CRITICAL_PROCESS_DIED", "Proses penting Windows berhenti tiba-tiba.", CrashCause.Unknown),
        new(0xFC, "ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", "Sebuah driver mencoba menjalankan kode dari memori terlarang.", CrashCause.Driver),
        new(0x116, "VIDEO_TDR_FAILURE", "Driver kartu grafis berhenti merespons dan gagal dipulihkan.", CrashCause.Driver),
        new(0x117, "VIDEO_TDR_TIMEOUT_DETECTED", "Driver kartu grafis berhenti merespons.", CrashCause.Driver),
        new(0x119, "VIDEO_SCHEDULER_INTERNAL_ERROR", "Driver kartu grafis mengalami kesalahan.", CrashCause.Driver),
        new(0x124, "WHEA_UNCORRECTABLE_ERROR", "Hardware melaporkan kesalahan fatal (CPU, RAM, atau suhu terlalu panas).", CrashCause.Hardware),
        new(0x12B, "FAULTY_HARDWARE_CORRUPTED_PAGE", "Windows menemukan data rusak di memori. Biasanya RAM atau SSD bermasalah.", CrashCause.Hardware),
        new(0x133, "DPC_WATCHDOG_VIOLATION", "Sebuah driver terlalu lama tidak merespons (sering driver penyimpanan atau firmware SSD).", CrashCause.Driver),
        new(0x139, "KERNEL_SECURITY_CHECK_FAILURE", "Windows menemukan data sistem yang rusak, biasanya oleh driver.", CrashCause.Driver),
        new(0x154, "UNEXPECTED_STORE_EXCEPTION", "Terjadi kesalahan saat membaca memori terkompresi, sering karena disk.", CrashCause.Unknown),
        new(0x157, "KERNEL_THREAD_PRIORITY_FLOOR_VIOLATION", "Sebuah driver mengatur prioritas proses sistem dengan cara yang salah.", CrashCause.Driver),
        new(0x1D8, "ATTEMPTED_SWITCH_FROM_DPC", "Sebuah driver melakukan operasi terlarang.", CrashCause.Driver),
    }.ToDictionary(b => b.Code);

    public static BugCheckInfo Get(uint code) =>
        Known.TryGetValue(code, out var info)
            ? info
            : new BugCheckInfo(code, "Kode " + Hex(code), "Windows berhenti karena kesalahan sistem.", CrashCause.Unknown);

    public static string Hex(uint code) => "0x" + code.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>Mengurai teks BugCheck dari event 1001, mis. "0x0000009f (0x3, 0xffff..., ...)".</summary>
    public static (uint Code, IReadOnlyList<ulong> Parameters)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(['(', ')', ',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !TryHex(parts[0], out var code))
        {
            return null;
        }

        var parameters = parts.Skip(1).Select(p => TryHex(p, out var v) ? v : (ulong?)null).OfType<ulong>().Take(4).ToList();
        return ((uint)code, parameters);
    }

    private static bool TryHex(string text, out ulong value)
    {
        var t = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return ulong.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
