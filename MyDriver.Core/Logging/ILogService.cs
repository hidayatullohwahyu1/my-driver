namespace MyDriver.Core.Logging;

/// <summary>Pencatat log aplikasi. Semua lapisan menulis log lewat antarmuka ini.</summary>
public interface ILogService
{
    void Write(LogLevel level, string message, Exception? exception = null);

    void Debug(string message) => Write(LogLevel.Debug, message);
    void Info(string message) => Write(LogLevel.Info, message);
    void Warning(string message, Exception? exception = null) => Write(LogLevel.Warning, message, exception);
    void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);
}
