using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MyDriver.Core.Devices;

namespace MyDriver.App.ViewModels;

/// <summary>Simpul pohon perangkat (kategori atau perangkat).</summary>
public abstract partial class DeviceTreeNode : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed class CategoryGroupViewModel : DeviceTreeNode
{
    public CategoryGroupViewModel(DeviceCategory category, IEnumerable<DeviceItemViewModel> devices)
    {
        Category = category;
        Devices = new ObservableCollection<DeviceItemViewModel>(devices);
        ProblemCount = Devices.Count(d => d.Status is DeviceStatus.Problem or DeviceStatus.Disabled);
    }

    public DeviceCategory Category { get; }

    public string Name => Category.DisplayName();

    public string Glyph => Category switch
    {
        DeviceCategory.Network => "",
        DeviceCategory.Audio => "",
        DeviceCategory.Display => "",
        DeviceCategory.Usb => "",
        DeviceCategory.Bluetooth => "",
        DeviceCategory.Storage => "",
        _ => "",
    };

    public ObservableCollection<DeviceItemViewModel> Devices { get; }

    public int ProblemCount { get; }

    public bool HasProblems => ProblemCount > 0;

    public string CountText => Devices.Count.ToString(CultureInfo.CurrentCulture);
}

/// <summary>Satu perangkat di pohon beserta teks yang siap ditampilkan di panel detail.</summary>
public sealed class DeviceItemViewModel : DeviceTreeNode
{
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");
    private const string Empty = "–";

    private readonly bool _signaturesChecked;

    public DeviceItemViewModel(DeviceInfo device, bool signaturesChecked)
    {
        Device = device;
        _signaturesChecked = signaturesChecked;
    }

    public DeviceInfo Device { get; }

    public string Name => Device.Name;

    public DeviceStatus Status => Device.Status;

    public string StatusText => Device.Status switch
    {
        DeviceStatus.Normal => "Berfungsi normal",
        DeviceStatus.Disabled => "Dinonaktifkan",
        DeviceStatus.NotConnected => "Tidak tersambung",
        _ => "Bermasalah",
    };

    public string CategoryText => Device.Category.DisplayName();

    public string ManufacturerText => Device.Manufacturer ?? Empty;

    public string DriverVersionText => Device.DriverVersion ?? (Device.HasDriver ? Empty : "Driver belum terpasang");

    public string DriverDateText => Device.DriverDate is { } date
        ? $"{date.ToString("d MMMM yyyy", Indonesian)} ({AgeText(date)})"
        : Empty;

    public string DriverProviderText => Device.DriverProvider ?? Empty;

    public string SignatureText => Device.IsSigned switch
    {
        true => Device.Signer is null ? "Bertanda tangan" : $"Bertanda tangan ({Device.Signer})",
        false => "Tidak bertanda tangan",
        _ when !Device.HasDriver => Empty,
        _ => _signaturesChecked ? "Tidak diketahui" : "Sedang diperiksa…",
    };

    public string ErrorCodeText => Device.ErrorCode == 0 ? "0 (tidak ada)" : Device.ErrorCode.ToString(CultureInfo.InvariantCulture);

    public string PnpClassText => Device.PnpClass ?? Empty;

    public string ServiceText => Device.Service ?? Empty;

    public string InfNameText => Device.InfName ?? Empty;

    public IReadOnlyList<string> HardwareIds => Device.HardwareIds;

    public bool HasHardwareIds => Device.HardwareIds.Count > 0;

    public string HardwareIdsText => HasHardwareIds ? string.Join(Environment.NewLine, Device.HardwareIds) : Empty;

    /// <summary>Cocok bila teks pencarian ada di nama, produsen, penyedia driver, atau ID perangkat.</summary>
    public bool Matches(string filter)
    {
        bool Has(string? value) => value?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true;

        return Has(Device.Name) || Has(Device.Manufacturer) || Has(Device.DriverProvider)
            || Has(Device.InstanceId) || Device.HardwareIds.Any(Has);
    }

    private static string AgeText(DateTime date)
    {
        var days = (DateTime.Today - date.Date).TotalDays;
        return days switch
        {
            < 0 => "tanggal di masa depan",
            < 31 => "bulan ini",
            < 365 => $"{(int)(days / 30.4)} bulan lalu",
            _ => $"{(int)(days / 365.25)} tahun lalu",
        };
    }
}
