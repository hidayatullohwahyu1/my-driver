namespace MyDriver.Windows.Commands;

/// <summary>
/// Satu-satunya jalan untuk menjalankan perintah eksternal (pnputil, dism, dll.).
/// Setiap perintah, output, dan kode keluar dicatat ke log.
/// </summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
