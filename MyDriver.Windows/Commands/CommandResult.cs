namespace MyDriver.Windows.Commands;

/// <summary>Hasil satu perintah eksternal.</summary>
public sealed record CommandResult(
    string CommandLine,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
