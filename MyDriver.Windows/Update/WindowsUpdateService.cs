using System.Runtime.InteropServices;
using MyDriver.Core.Devices;
using MyDriver.Core.Logging;
using MyDriver.Windows.Drivers;

namespace MyDriver.Windows.Update;

/// <summary>Satu driver yang ditawarkan Windows Update.</summary>
public sealed record DriverUpdate(
    string UpdateId,
    string Title,
    string? HardwareId,
    string? Provider,
    string? Model,
    string? DriverClass,
    DateTime? DriverDate,
    long DownloadBytes);

public sealed record DriverUpdateSearch(bool Success, string? Error, IReadOnlyList<DriverUpdate> Updates, DateTime SearchedAt);

/// <summary>
/// Mencari dan memasang driver resmi lewat Windows Update Agent (COM <c>Microsoft.Update.Session</c>).
/// Mencari tidak butuh Administrator; memasang butuh. Hasil pencarian disimpan 30 menit karena
/// pencarian bisa makan 1–3 menit.
/// </summary>
public sealed class WindowsUpdateService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

    /// <summary>ssWindowsUpdate: selalu ke Windows Update online, bukan WSUS kantor.</summary>
    private const int ServerSelectionWindowsUpdate = 2;

    private readonly ILogService _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DriverUpdateSearch? _cache;

    public WindowsUpdateService(ILogService log)
    {
        _log = log;
    }

    /// <summary>Mencari semua driver yang belum terpasang (dipakai ulang selama 30 menit).</summary>
    public async Task<DriverUpdateSearch> SearchAsync(bool forceRefresh = false)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _cache is { Success: true } cached && DateTime.Now - cached.SearchedAt < CacheLifetime)
            {
                return cached;
            }
            _cache = await RunStaAsync(Search).ConfigureAwait(false);
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Driver dari hasil pencarian yang cocok dengan Hardware ID perangkat, terbaru lebih dulu.</summary>
    public static IReadOnlyList<DriverUpdate> MatchDevice(IEnumerable<DriverUpdate> updates, DeviceInfo device)
    {
        var ids = new HashSet<string>(device.HardwareIds.Concat(device.CompatibleIds), StringComparer.OrdinalIgnoreCase);
        return updates
            .Where(u => u.HardwareId is { } id && ids.Contains(id))
            .Where(u => device.DriverDate is not { } current || u.DriverDate is not { } offered || offered.Date >= current.Date)
            .OrderByDescending(u => u.DriverDate)
            .ToList();
    }

    /// <summary>Mengunduh lalu memasang satu driver. Butuh Administrator.</summary>
    public Task<DriverOperationResult> InstallAsync(DriverUpdate update) => RunStaAsync(() => Install(update));

    private DriverUpdateSearch Search()
    {
        _log.Info("Mencari driver di Windows Update…");
        try
        {
            dynamic session = CreateCom("Microsoft.Update.Session");
            session.ClientApplicationID = "My Driver!";
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = ServerSelectionWindowsUpdate;
            searcher.Online = true;
            dynamic result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");

            var list = new List<DriverUpdate>();
            foreach (dynamic u in result.Updates)
            {
                list.Add(new DriverUpdate(
                    (string)u.Identity.UpdateID,
                    (string)u.Title,
                    (string?)u.DriverHardwareID,
                    (string?)u.DriverProvider,
                    (string?)u.DriverModel,
                    (string?)u.DriverClass,
                    u.DriverVerDate is DateTime d ? d : (DateTime?)null,
                    Convert.ToInt64(u.MaxDownloadSize)));
            }

            _log.Info($"Windows Update menawarkan {list.Count} driver: {string.Join("; ", list.Select(u => u.Title))}");
            return new DriverUpdateSearch(true, null, list, DateTime.Now);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
        {
            _log.Warning("Pencarian Windows Update gagal.", ex);
            return new DriverUpdateSearch(false, Describe(ex), [], DateTime.Now);
        }
    }

    private DriverOperationResult Install(DriverUpdate update)
    {
        _log.Info($"Memasang dari Windows Update: {update.Title} ({update.UpdateId})");
        try
        {
            dynamic session = CreateCom("Microsoft.Update.Session");
            session.ClientApplicationID = "My Driver!";
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = ServerSelectionWindowsUpdate;
            dynamic found = searcher.Search($"UpdateID='{update.UpdateId}'");
            if (found.Updates.Count == 0)
            {
                return new DriverOperationResult(false, "Driver ini tidak lagi ditawarkan Windows Update. Cari ulang.");
            }

            dynamic item = found.Updates.Item(0);
            if (!(bool)item.EulaAccepted)
            {
                item.AcceptEula();
            }
            dynamic collection = CreateCom("Microsoft.Update.UpdateColl");
            collection.Add(item);

            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = collection;
            dynamic download = downloader.Download();
            if ((int)download.ResultCode is not (2 or 3))
            {
                return new DriverOperationResult(false, $"Driver gagal diunduh (kode hasil {(int)download.ResultCode}). Periksa koneksi internet.");
            }

            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic install = installer.Install();
            var code = (int)install.ResultCode;
            var reboot = (bool)install.RebootRequired;
            _log.Info($"Hasil pemasangan Windows Update: kode {code}, perlu restart {reboot}.");
            return code is 2 or 3
                ? new DriverOperationResult(true,
                    reboot ? $"{update.Title} berhasil dipasang. Restart Windows untuk menyelesaikan." : $"{update.Title} berhasil dipasang.",
                    RebootRequired: reboot)
                : new DriverOperationResult(false, $"Pemasangan gagal (kode hasil {code}).");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
        {
            _log.Warning("Pemasangan dari Windows Update gagal.", ex);
            return new DriverOperationResult(false, Describe(ex));
        }
    }

    private static object CreateCom(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;

    /// <summary>Pesan galat Windows Update yang umum, dalam bahasa sederhana.</summary>
    internal static string Describe(Exception ex) => (uint)ex.HResult switch
    {
        0x8024402C or 0x80072EE7 or 0x80072EFD or 0x80072EFE or 0x8024401C or 0x80244022 =>
            "Tidak bisa terhubung ke Windows Update. Periksa koneksi internet lalu coba lagi.",
        0x80070422 => "Layanan Windows Update sedang dimatikan. Aktifkan layanan \"Windows Update\" lalu coba lagi.",
        0x80240024 => "Windows Update tidak menemukan pembaruan.",
        0x8024002E => "Windows Update dibatasi oleh kebijakan kantor/sekolah (WSUS).",
        0x80070005 => "Butuh hak Administrator.",
        var hr => $"Windows Update gagal (0x{hr:X8}).",
    };

    /// <summary>Windows Update Agent lebih stabil dipanggil dari thread STA.</summary>
    private static Task<T> RunStaAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Windows Update",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
