namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// A running, human-readable log of the steps taken while pairing/configuring a printer, shown
/// live in the UI's activity log panel. Entries may be appended from background threads (Bluetooth
/// callbacks, connection retries), so implementations must be safe to call from any thread.
/// </summary>
public interface IAppLog
{
    event EventHandler<LogEntry>? EntryLogged;

    IReadOnlyList<LogEntry> Entries { get; }

    void Log(string message, LogLevel level = LogLevel.Info);
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record LogEntry(DateTimeOffset Timestamp, string Message, LogLevel Level);
