using System.Collections.Concurrent;
using Zebra.Printer.Configurator.Core.Abstractions;

namespace Zebra.Printer.Configurator.Core.Logging;

/// <summary>
/// In-memory, thread-safe log store. No Android dependency - entries are appended from multiple
/// threads (Bluetooth callbacks, background connection attempts), so both the backing store and the
/// event raise are safe to call from any thread; subscribers (the UI) marshal back to their own
/// thread via InvokeAsync when they react.
/// </summary>
public sealed class AppLog : IAppLog
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public event EventHandler<LogEntry>? EntryLogged;

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(DateTimeOffset.Now, message, level);
        _entries.Enqueue(entry);
        EntryLogged?.Invoke(this, entry);
    }
}
