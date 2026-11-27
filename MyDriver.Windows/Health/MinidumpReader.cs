using System.Buffers.Binary;
using System.Text;

namespace MyDriver.Windows.Health;

/// <summary>Satu modul kernel yang dimuat saat crash.</summary>
public sealed record DumpDriver(string Name, ulong Base, uint Size)
{
    public bool Contains(ulong address) => address >= Base && address < Base + Size;
}

public sealed record MinidumpInfo(
    uint BugCheckCode,
    IReadOnlyList<ulong> Parameters,
    uint DumpType,
    IReadOnlyList<DumpDriver> Drivers,
    string? SuspectDriver,
    IReadOnlyList<string> StackDrivers);

/// <summary>
/// Membaca minidump kernel 64-bit (C:\Windows\Minidump\*.dmp) tanpa debugger:
/// kode blue screen, daftar driver yang dimuat, dan driver tersangka.
///
/// Header (<c>DUMP_HEADER64</c>, "PAGEDU64") formatnya stabil. Daftar driver ada di bagian triage
/// (<c>TRIAGE_DUMP64</c> di offset 0x2000); ukuran entrinya dikalibrasi dan hasilnya hanya dipakai
/// bila hampir semua entri valid. Bila ragu, tersangka dikosongkan, bukan ditebak.
/// Tersangka = driver non-inti pertama yang memuat alamat kejadian: alamat exception, RIP,
/// parameter blue screen, lalu isi tumpukan dari atas.
/// </summary>
public static class MinidumpReader
{
    private const int HeaderSize = 0x2000;
    private const uint TriageDumpType = 4;
    private const ulong KernelSpaceStart = 0xFFFF_8000_0000_0000;
    private const long MaxFileSize = 64L * 1024 * 1024;

    /// <summary>Inti Windows dan kerangka driver: jarang penyebab sebenarnya bila ada driver lain di tumpukan.</summary>
    private static readonly HashSet<string> CoreModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "ntoskrnl.exe", "ntkrnlmp.exe", "ntkrnlpa.exe", "ntkrpamp.exe",
        "hal.dll", "halmacpi.dll", "halacpi.dll", "kdcom.dll", "kd.dll", "ci.dll", "pshed.dll",
        "clfs.sys", "tm.sys", "werkernel.sys", "fltmgr.sys", "wdf01000.sys", "wdfldr.sys",
        "ndis.sys", "storport.sys", "ataport.sys", "acpi.sys", "pci.sys", "msrpc.sys", "cng.sys",
    };

    public static MinidumpInfo? Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxFileSize)
        {
            return null;
        }
        return Parse(File.ReadAllBytes(path));
    }

    public static MinidumpInfo? Parse(byte[] dump)
    {
        if (dump.Length < HeaderSize + 0x50 || !Ascii(dump, 0, "PAGE") || !Ascii(dump, 4, "DU64"))
        {
            return null;
        }

        var code = U32(dump, 0x38);
        ulong[] parameters = [U64(dump, 0x40), U64(dump, 0x48), U64(dump, 0x50), U64(dump, 0x58)];
        var dumpType = U32(dump, 0xF98);

        var drivers = dumpType == TriageDumpType ? ReadDrivers(dump) : [];
        if (drivers.Count == 0)
        {
            return new MinidumpInfo(code, parameters, dumpType, drivers, null, []);
        }

        var hits = new List<string>();
        void Check(ulong address)
        {
            if (address < KernelSpaceStart)
            {
                return;
            }
            var owner = drivers.FirstOrDefault(d => d.Contains(address));
            if (owner is not null && (hits.Count == 0 || !hits[^1].Equals(owner.Name, StringComparison.OrdinalIgnoreCase)))
            {
                hits.Add(owner.Name);
            }
        }

        Check(U64(dump, 0xF00 + 0x10)); // EXCEPTION_RECORD64.ExceptionAddress
        Check(U64(dump, 0x348 + 0xF8)); // CONTEXT.Rip
        foreach (var p in parameters)
        {
            Check(p);
        }
        foreach (var value in StackValues(dump))
        {
            Check(value);
        }

        var ordered = hits.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var suspect = ordered.FirstOrDefault(n => !CoreModules.Contains(n));
        return new MinidumpInfo(code, parameters, dumpType, drivers, suspect, ordered.Take(8).ToList());
    }

    /// <summary>Isi tumpukan thread yang crash (TRIAGE_DUMP64.CallStackOffset/SizeOfCallStack).</summary>
    private static IEnumerable<ulong> StackValues(byte[] dump)
    {
        var offset = U32(dump, HeaderSize + 0x28);
        var size = U32(dump, HeaderSize + 0x2C);
        if (offset < HeaderSize || size == 0 || size > 1024 * 1024 || (long)offset + size > dump.Length)
        {
            yield break;
        }
        for (var i = offset; i + 8 <= offset + size; i += 8)
        {
            yield return U64(dump, (int)i);
        }
    }

    /// <summary>
    /// Daftar driver (TRIAGE_DUMP64.DriverListOffset/DriverCount). Setiap entri: ULONG offset nama,
    /// lalu KLDR_DATA_TABLE_ENTRY64 (DllBase +0x30, SizeOfImage +0x40) mulai offset 8.
    /// Ukuran entri dan cara membaca offset nama dikalibrasi.
    /// </summary>
    internal static IReadOnlyList<DumpDriver> ReadDrivers(byte[] dump)
    {
        var listOffset = U32(dump, HeaderSize + 0x30);
        var count = U32(dump, HeaderSize + 0x34);
        var poolOffset = U32(dump, HeaderSize + 0x38);
        if (count is 0 or > 4096 || listOffset < HeaderSize || listOffset >= dump.Length)
        {
            return [];
        }

        for (var stride = 0x60; stride <= 0x140; stride += 8)
        {
            foreach (var poolRelative in new[] { false, true })
            {
                var drivers = new List<DumpDriver>();
                for (var i = 0; i < count; i++)
                {
                    var entry = (long)listOffset + (long)i * stride;
                    if (entry + 8 + 0x48 > dump.Length)
                    {
                        break;
                    }

                    var nameOffset = (long)U32(dump, (int)entry) + (poolRelative ? poolOffset : 0);
                    var name = ReadDumpString(dump, nameOffset);
                    var dllBase = U64(dump, (int)entry + 8 + 0x30);
                    var size = U32(dump, (int)entry + 8 + 0x40);
                    if (name is not null && dllBase >= KernelSpaceStart && size is > 0 and < 0x2000_0000)
                    {
                        drivers.Add(new DumpDriver(name, dllBase, size));
                    }
                }

                if (drivers.Count >= Math.Max(3, (int)(count * 0.9)))
                {
                    return drivers;
                }
            }
        }
        return [];
    }

    /// <summary>DUMP_STRING: ULONG panjang (karakter) lalu teks UTF-16. Hanya nama file modul yang diterima.</summary>
    private static string? ReadDumpString(byte[] dump, long offset)
    {
        if (offset < HeaderSize || offset + 4 > dump.Length)
        {
            return null;
        }
        var length = U32(dump, (int)offset);
        if (length is 0 or > 260 || offset + 4 + length * 2 > dump.Length)
        {
            return null;
        }

        var text = Encoding.Unicode.GetString(dump, (int)offset + 4, (int)length * 2).TrimEnd('\0');
        if (text.Length == 0 || text.Any(c => c < 0x20 || c > 0x7E))
        {
            return null;
        }
        var name = text[(text.LastIndexOf('\\') + 1)..];
        return name.EndsWith(".sys", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name
            : null;
    }

    private static bool Ascii(byte[] data, int offset, string expected) =>
        Encoding.ASCII.GetString(data, offset, expected.Length) == expected;

    private static uint U32(byte[] data, int offset) =>
        offset + 4 <= data.Length ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) : 0;

    private static ulong U64(byte[] data, int offset) =>
        offset + 8 <= data.Length ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset)) : 0;
}
