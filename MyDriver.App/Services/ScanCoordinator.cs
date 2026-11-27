using CommunityToolkit.Mvvm.ComponentModel;
using MyDriver.Core.Devices;
using MyDriver.Core.Logging;

namespace MyDriver.App.Services;

/// <summary>
/// Menyimpan hasil scan terakhir dan memastikan hanya satu scan berjalan.
/// Dipakai bersama oleh Beranda dan halaman Perangkat.
/// </summary>
public sealed partial class ScanCoordinator : ObservableObject
{
    private readonly IDeviceScanner _scanner;
    private readonly ILogService _log;
    private int _generation;

    public ScanCoordinator(IDeviceScanner scanner, ILogService log)
    {
        _scanner = scanner;
        _log = log;
    }

    [ObservableProperty]
    private ScanResult? _lastResult;

    /// <summary>Tahap cepat sedang berjalan.</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>Tahap latar (tanda tangan driver) sedang berjalan.</summary>
    [ObservableProperty]
    private bool _isCheckingSignatures;

    [ObservableProperty]
    private string? _lastError;

    public async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        var generation = ++_generation;
        IsScanning = true;
        LastError = null;
        ScanResult result;
        try
        {
            result = await _scanner.ScanAsync();
            LastResult = result;
        }
        catch (Exception ex)
        {
            _log.Error("Scan perangkat gagal.", ex);
            LastError = $"Scan gagal: {ex.Message}";
            return;
        }
        finally
        {
            IsScanning = false;
        }

        IsCheckingSignatures = true;
        try
        {
            var withSignatures = await _scanner.AddSignaturesAsync(result);
            if (generation == _generation)
            {
                LastResult = withSignatures;
            }
        }
        catch (Exception ex)
        {
            // Hasil scan tetap dipakai; hanya info tanda tangan yang kosong.
            _log.Warning("Pemeriksaan tanda tangan driver gagal.", ex);
        }
        finally
        {
            if (generation == _generation)
            {
                IsCheckingSignatures = false;
            }
        }
    }
}
