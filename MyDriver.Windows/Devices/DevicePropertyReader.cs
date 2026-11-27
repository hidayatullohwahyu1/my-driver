using System.Runtime.InteropServices;
using MyDriver.Core.Devices;

namespace MyDriver.Windows.Devices;

/// <summary>
/// Membaca properti driver langsung dari Configuration Manager (cfgmgr32.dll).
/// Jauh lebih cepat daripada WMI <c>Win32_PnPSignedDriver</c> (milidetik, bukan detik),
/// tetapi tidak menyediakan info tanda tangan.
/// </summary>
public static class DevicePropertyReader
{
    private const int CrSuccess = 0x00;
    private const int CrBufferSmall = 0x1A;
    private const uint CmLocateDevnodePhantom = 0x1;
    private const uint DevpropTypeString = 0x12;
    private const uint DevpropTypeFiletime = 0x10;

    // DEVPKEY_Device_Driver* (devpkey.h): {a8b865dd-2e3d-4094-ad97-e593a70c75d6}
    private static readonly Guid DriverFmtId = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
    private static readonly DevPropKey DriverDate = new(DriverFmtId, 2);
    private static readonly DevPropKey DriverVersion = new(DriverFmtId, 3);
    private static readonly DevPropKey DriverDesc = new(DriverFmtId, 4);
    private static readonly DevPropKey DriverInfPath = new(DriverFmtId, 5);
    private static readonly DevPropKey DriverProvider = new(DriverFmtId, 9);

    /// <summary>Mengembalikan data driver untuk tiap ID perangkat yang punya driver terpasang.</summary>
    public static IReadOnlyList<SignedDriverRecord> ReadDrivers(IEnumerable<string> deviceIds)
    {
        var list = new List<SignedDriverRecord>();
        foreach (var id in deviceIds)
        {
            if (string.IsNullOrWhiteSpace(id)
                || CM_Locate_DevNodeW(out var devInst, id, CmLocateDevnodePhantom) != CrSuccess)
            {
                continue;
            }

            var version = ReadString(devInst, DriverVersion);
            var inf = ReadString(devInst, DriverInfPath);
            if (version is null && inf is null)
            {
                continue; // tidak ada driver terpasang
            }

            list.Add(new SignedDriverRecord(
                DeviceId: id,
                DeviceName: ReadString(devInst, DriverDesc),
                DeviceClass: null,
                DriverVersion: version,
                DriverDate: ReadFileTime(devInst, DriverDate)?.ToString("yyyyMMdd"),
                DriverProvider: ReadString(devInst, DriverProvider),
                InfName: inf,
                IsSigned: null,
                Signer: null));
        }
        return list;
    }

    private static string? ReadString(uint devInst, DevPropKey key)
    {
        var bytes = Read(devInst, key, DevpropTypeString);
        if (bytes is null)
        {
            return null;
        }
        var text = System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
        return text.Length == 0 ? null : text;
    }

    private static DateTime? ReadFileTime(uint devInst, DevPropKey key)
    {
        var bytes = Read(devInst, key, DevpropTypeFiletime);
        if (bytes is not { Length: 8 })
        {
            return null;
        }
        var fileTime = BitConverter.ToInt64(bytes, 0);
        try
        {
            return DateTime.FromFileTimeUtc(fileTime).Date;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[]? Read(uint devInst, DevPropKey key, uint expectedType)
    {
        uint size = 0;
        var result = CM_Get_DevNode_PropertyW(devInst, ref key, out var type, null, ref size, 0);
        if (result != CrBufferSmall || size == 0 || type != expectedType)
        {
            return null;
        }

        var buffer = new byte[size];
        result = CM_Get_DevNode_PropertyW(devInst, ref key, out type, buffer, ref size, 0);
        return result == CrSuccess && type == expectedType ? buffer : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid fmtId, uint pid)
    {
        public Guid FmtId = fmtId;
        public uint Pid = pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint devInst, ref DevPropKey propertyKey, out uint propertyType, byte[]? buffer, ref uint bufferSize, uint flags);
}
