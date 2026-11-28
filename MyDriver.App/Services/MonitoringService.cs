using MyDriver.Core.Logging;
using MyDriver.Core.Repair;
using MyDriver.Windows.Drivers;
using MyDriver.Windows.Monitoring;

namespace MyDriver.App.Services;

/// <summary>Menyalakan dan mematikan pemantauan otomatis (tugas Task Scheduler) dari Pengaturan dan Beranda.</summary>
public sealed class MonitoringService
{
    private readonly ScheduledTaskService _tasks;
    private readonly WorkspaceService _workspace;
    private readonly DriverActions _actions;
    private readonly ILogService _log;

    public MonitoringService(ScheduledTaskService tasks, WorkspaceService workspace, DriverActions actions, ILogService log)
    {
        _tasks = tasks;
        _workspace = workspace;
        _actions = actions;
        _log = log;
    }

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MyDriver.exe");

    public static bool IsOnRemovableDrive => ScheduledTaskService.IsOnRemovableDrive(ExePath);

    /// <summary>Path exe tugas, atau null bila pemantauan mati.</summary>
    public Task<string?> GetTaskExePathAsync() => _tasks.GetTaskExePathAsync();

    /// <summary>Null bila tidak dijalankan (butuh Administrator).</summary>
    public async Task<DriverOperationResult?> EnableAsync(int dailyHour)
    {
        if (!_actions.CheckAdministrator("Mengaktifkan pemantauan otomatis"))
        {
            return null;
        }
        var result = await _tasks.EnableAsync(ExePath, dailyHour);
        Record("Pemantauan otomatis diaktifkan", result);
        return result;
    }

    public async Task<DriverOperationResult?> DisableAsync()
    {
        if (!_actions.CheckAdministrator("Mematikan pemantauan otomatis"))
        {
            return null;
        }
        var result = await _tasks.DisableAsync();
        Record("Pemantauan otomatis dimatikan", result);
        return result;
    }

    private void Record(string title, DriverOperationResult result)
    {
        _log.Info($"{title}: {(result.Success ? "berhasil" : "gagal")} - {result.Message}");
        _workspace.AddHistory(new HistoryEntry
        {
            Time = DateTime.Now,
            Kind = HistoryKind.Action,
            Title = title,
            Success = result.Success,
            Message = result.Message,
            Commands = result.Command is { } c ? [c.CommandLine] : [],
        });
    }
}
