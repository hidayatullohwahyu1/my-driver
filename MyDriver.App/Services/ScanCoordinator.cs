using CommunityToolkit.Mvvm.ComponentModel;
using MyDriver.Core.Devices;
using MyDriver.Core.Diagnosis;
using MyDriver.Core.Logging;
using MyDriver.Windows.Health;

namespace MyDriver.App.Services;

/// <summary>
/// Menyimpan hasil scan terakhir dan memastikan hanya satu scan berjalan.
/// Dipakai bersama oleh Beranda dan halaman Perangkat.
/// </summary>
public sealed partial class ScanCoordinator : ObservableObject
{
    private readonly IDeviceScanner _scanner;
    private readonly SystemHealthReader? _health;
    private readonly ILogService _log;
    private int _generation;

    public ScanCoordinator(IDeviceScanner scanner, ILogService log, SystemHealthReader? health = null)
    {
        _scanner = scanner;
        _health = health;
        _log = log;
    }

    /// <summary>Log kejadian dan blue screen, dibaca di latar setiap kali scan.</summary>
    [ObservableProperty]
    private SystemHealth? _systemHealth;

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
        var healthTask = ReadHealthAsync(result, generation);
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
        await healthTask;
    }

    private async Task ReadHealthAsync(ScanResult result, int generation)
    {
        if (_health is null)
        {
            return;
        }
        try
        {
            var health = await _health.ReadAsync(result.Devices.Select(d => d.Service));
            if (generation == _generation)
            {
                SystemHealth = health;
            }
        }
        catch (Exception ex)
        {
            _log.Warning("Log kejadian Windows tidak bisa dibaca.", ex);
        }
    }
}
