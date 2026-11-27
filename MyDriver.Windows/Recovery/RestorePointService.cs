using System.Management;
using MyDriver.Core.Logging;

namespace MyDriver.Windows.Recovery;

public enum RestorePointStatus
{
    Created,

    /// <summary>Windows melewati pembuatan karena sudah ada restore point dalam 24 jam terakhir.</summary>
    RecentExists,

    /// <summary>Gagal, biasanya karena System Protection mati.</summary>
    Failed,
}

public sealed record RestorePointResult(RestorePointStatus Status, string Message, DateTime? PointTime = null)
{
    public bool IsSafe => Status is RestorePointStatus.Created or RestorePointStatus.RecentExists;
}

/// <summary>Membuat restore point lewat WMI <c>root\default:SystemRestore</c>. Wajib Administrator.</summary>
public sealed class RestorePointService
{
    private const int DeviceDriverInstall = 10;
    private const int BeginSystemChange = 100;

    private readonly ILogService _log;

    public RestorePointService(ILogService log)
    {
        _log = log;
    }

    public Task<RestorePointResult> CreateAsync(string description) => Task.Run(() => Create(description));

    private RestorePointResult Create(string description)
    {
        _log.Info($"Membuat restore point: {description}");
        try
        {
            var before = LatestPoint();
            var scope = new ManagementScope(@"\\.\root\default");
            using var systemRestore = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using var input = systemRestore.GetMethodParameters("CreateRestorePoint");
            input["Description"] = description;
            input["RestorePointType"] = DeviceDriverInstall;
            input["EventType"] = BeginSystemChange;
            using var output = systemRestore.InvokeMethod("CreateRestorePoint", input, null);
            var code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u);

            var after = LatestPoint();
            if (after is not null && (before is null || after.Value.Sequence > before.Value.Sequence))
            {
                _log.Info($"Restore point dibuat (nomor {after.Value.Sequence}).");
                return new RestorePointResult(RestorePointStatus.Created, "Restore point berhasil dibuat.", after.Value.Time);
            }

            if (code == 0 && after is not null)
            {
                _log.Info($"Restore point dilewati Windows; yang terbaru dibuat {after.Value.Time:g}.");
                return new RestorePointResult(
                    RestorePointStatus.RecentExists,
                    $"Windows tidak membuat restore point baru karena sudah ada yang dibuat {after.Value.Time:dd-MM-yyyy HH.mm}. Restore point itu bisa dipakai untuk kembali.",
                    after.Value.Time);
            }

            _log.Warning($"Restore point gagal dibuat (kode {code}).");
            return new RestorePointResult(RestorePointStatus.Failed, FailureMessage(code));
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _log.Warning("Restore point gagal dibuat.", ex);
            return new RestorePointResult(RestorePointStatus.Failed, $"Restore point gagal dibuat: {ex.Message}");
        }
    }

    private static string FailureMessage(uint code) => code switch
    {
        1058 or 1 => "Restore point gagal dibuat karena System Protection (Perlindungan Sistem) di drive C: sedang mati.",
        _ => $"Restore point gagal dibuat (kode {code}). System Protection mungkin sedang mati.",
    };

    private static (uint Sequence, DateTime Time)? LatestPoint()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber, CreationTime FROM SystemRestore");
            using var results = searcher.Get();
            (uint, DateTime)? latest = null;
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    var seq = Convert.ToUInt32(item["SequenceNumber"]);
                    var time = ManagementDateTimeConverter.ToDateTime((string)item["CreationTime"]);
                    if (latest is null || seq > latest.Value.Item1)
                    {
                        latest = (seq, time);
                    }
                }
            }
            return latest;
        }
        catch (ManagementException)
        {
            return null;
        }
    }
}
