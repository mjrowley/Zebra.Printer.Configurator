namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Opaque handle to a single already-open printer connection, shared across several steps of a
/// workflow (e.g. applying WLAN config, enabling PDF Direct, and restarting all run over one
/// Bluetooth connection instead of each reconnecting). Deliberately exposes nothing but disposal -
/// Core can't reference the Zebra SDK's Connection type, so the actual read/write surface lives on
/// the concrete Infrastructure.Android implementation.
/// </summary>
public interface IPrinterConnectionSession : IAsyncDisposable
{
}
