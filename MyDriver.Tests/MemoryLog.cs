using System.Collections.Concurrent;
using MyDriver.Core.Logging;

namespace MyDriver.Tests;

/// <summary>Logger palsu untuk test: menyimpan semua entri di memori.</summary>
internal sealed class MemoryLog : ILogService
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public void Write(LogLevel level, string message, Exception? exception = null) =>
        Entries.Enqueue((level, message));
}
