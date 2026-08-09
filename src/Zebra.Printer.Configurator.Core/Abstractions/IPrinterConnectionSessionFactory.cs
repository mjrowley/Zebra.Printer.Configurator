using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Opens a single printer connection (Bluetooth or WiFi, whichever IPrinterConnectionModeProvider
/// currently selects) for a caller to share across multiple steps, rather than each step
/// independently opening and closing its own.
/// </summary>
public interface IPrinterConnectionSessionFactory
{
    Task<IPrinterConnectionSession> OpenAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
