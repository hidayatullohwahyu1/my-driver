using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using MyDriver.Windows.Commands;
using MyDriver.Windows.Drivers;

namespace MyDriver.Windows.Monitoring;

/// <summary>
/// Tugas Task Scheduler untuk pemantauan otomatis: menjalankan <c>MyDriver.exe --background</c>
/// saat logon (tertunda 2 menit) dan sekali sehari.
/// RunLevel = HighestAvailable: exe memakai manifest highestAvailable, dan untuk akun admin Windows
/// menolak menjalankannya dari tugas tanpa hak tertinggi (galat 740). Task Scheduler menjalankannya
/// tanpa dialog UAC; scan latar tetap hanya membaca. Membuat tugas ini butuh Administrator.
/// </summary>
public sealed partial class ScheduledTaskService
{
    public const string TaskName = @"\MyDriver\Pemantauan";
    public const string BackgroundArgument = "--background";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ICommandRunner _runner;

    public ScheduledTaskService(ICommandRunner runner)
    {
        _runner = runner;
    }

    /// <summary>Path exe yang dipakai tugas, atau null bila tugas belum ada.</summary>
    public async Task<string?> GetTaskExePathAsync()
    {
        var result = await _runner.RunAsync("schtasks", ["/Query", "/TN", TaskName, "/XML"], Timeout).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return null;
        }
        var match = CommandElement().Match(result.StandardOutput);
        return match.Success ? SecurityElementUnescape(match.Groups["path"].Value.Trim().Trim('"')) : string.Empty;
    }

    public async Task<DriverOperationResult> EnableAsync(string exePath, int dailyHour)
    {
        var xmlFile = Path.Combine(Path.GetTempPath(), $"MyDriver-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks membaca XML tugas sebagai UTF-16.
            await File.WriteAllTextAsync(xmlFile, BuildTaskXml(exePath, dailyHour, CurrentUser()), Encoding.Unicode).ConfigureAwait(false);
            var result = await _runner.RunAsync("schtasks", ["/Create", "/TN", TaskName, "/XML", xmlFile, "/F"], Timeout).ConfigureAwait(false);
            return result.Succeeded
                ? new DriverOperationResult(true, $"Pemantauan otomatis aktif: scan saat Windows dibuka dan setiap hari pukul {dailyHour:00}.00.", result)
                : new DriverOperationResult(false, $"Tugas terjadwal gagal dibuat (kode keluar {result.ExitCode}). {result.StandardError.Trim()}", result);
        }
        finally
        {
            TryDelete(xmlFile);
        }
    }

    public async Task<DriverOperationResult> DisableAsync()
    {
        var result = await _runner.RunAsync("schtasks", ["/Delete", "/TN", TaskName, "/F"], Timeout).ConfigureAwait(false);
        return result.Succeeded
            ? new DriverOperationResult(true, "Pemantauan otomatis dimatikan.", result)
            : new DriverOperationResult(false, $"Tugas terjadwal gagal dihapus (kode keluar {result.ExitCode}).", result);
    }

    /// <summary>Isi XML tugas (format Task Scheduler 1.2, Windows 7 ke atas).</summary>
    public static string BuildTaskXml(string exePath, int dailyHour, string userId)
    {
        dailyHour = Math.Clamp(dailyHour, 0, 23);
        var start = DateTime.Today.AddHours(dailyHour).ToString("yyyy-MM-dd'T'HH:mm:ss");
        var exe = SecurityElement.Escape(exePath);
        var dir = SecurityElement.Escape(Path.GetDirectoryName(exePath) ?? string.Empty);
        var user = SecurityElement.Escape(userId);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>My Driver!</Author>
                <Description>Scan driver otomatis oleh My Driver! dan notifikasi bila ada masalah baru.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT2M</Delay>
                </LogonTrigger>
                <CalendarTrigger>
                  <StartBoundary>{start}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT30M</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{exe}"</Command>
                  <Arguments>{BackgroundArgument}</Arguments>
                  <WorkingDirectory>{dir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>True bila exe berada di flashdisk/drive lepas-pasang (tugas akan gagal saat dicabut).</summary>
    public static bool IsOnRemovableDrive(string exePath)
    {
        try
        {
            var root = Path.GetPathRoot(exePath);
            return root is not null && new DriveInfo(root).DriveType == DriveType.Removable;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }

    private static string CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    private static string SecurityElementUnescape(string text) =>
        text.Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
    }

    [GeneratedRegex(@"<Command>(?<path>[^<]*)</Command>", RegexOptions.IgnoreCase)]
    private static partial Regex CommandElement();
}
