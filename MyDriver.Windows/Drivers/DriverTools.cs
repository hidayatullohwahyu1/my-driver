using MyDriver.Windows.Commands;

namespace MyDriver.Windows.Drivers;

/// <summary>Pembungkus <c>dism.exe</c> untuk ekspor driver.</summary>
public sealed class DismService
{
    public static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(30);

    private readonly ICommandRunner _runner;

    public DismService(ICommandRunner runner)
    {
        _runner = runner;
    }

    /// <summary>Mengekspor semua driver pihak ketiga ke folder tujuan. Wajib Administrator.</summary>
    public Task<CommandResult> ExportDriversAsync(string destination, CancellationToken cancellationToken = default) =>
        _runner.RunAsync("dism", ExportArguments(destination), ExportTimeout, cancellationToken);

    internal static IReadOnlyList<string> ExportArguments(string destination) =>
        ["/English", "/Online", "/Export-Driver", $"/Destination:{destination}"];
}

/// <summary>Hasil <c>pnputil /add-driver</c> yang sudah ditafsirkan.</summary>
public enum PnpUtilOutcome
{
    Success,
    SuccessRebootRequired,

    /// <summary>Paket sudah ada; tidak ada perangkat yang driver-nya berubah.</summary>
    NoChange,
    Failed,
}

/// <summary>Pembungkus <c>pnputil.exe</c>.</summary>
public sealed class PnpUtilService
{
    public static readonly TimeSpan AddDriverTimeout = TimeSpan.FromMinutes(15);

    private const int ErrorSuccessRebootRequired = 3010;
    private const int ErrorNoMoreItems = 259;

    private readonly ICommandRunner _runner;

    public PnpUtilService(ICommandRunner runner)
    {
        _runner = runner;
    }

    /// <summary>Menambahkan paket driver ke DriverStore dan memasangnya ke perangkat yang cocok. Wajib Administrator.</summary>
    public Task<CommandResult> AddDriverAsync(string infPathOrPattern, bool includeSubdirectories, CancellationToken cancellationToken = default) =>
        _runner.RunAsync("pnputil", AddDriverArguments(infPathOrPattern, includeSubdirectories), AddDriverTimeout, cancellationToken);

    internal static IReadOnlyList<string> AddDriverArguments(string infPathOrPattern, bool includeSubdirectories) =>
        includeSubdirectories
            ? ["/add-driver", infPathOrPattern, "/subdirs", "/install"]
            : ["/add-driver", infPathOrPattern, "/install"];

    public static PnpUtilOutcome Interpret(CommandResult result) => result switch
    {
        { TimedOut: true } => PnpUtilOutcome.Failed,
        { ExitCode: 0 } => PnpUtilOutcome.Success,
        { ExitCode: ErrorSuccessRebootRequired } => PnpUtilOutcome.SuccessRebootRequired,
        { ExitCode: ErrorNoMoreItems } => PnpUtilOutcome.NoChange,
        _ => PnpUtilOutcome.Failed,
    };
}
