using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Reads a printer's firmware version, web interface state, and full WLAN configuration list in
/// one connection - see PrinterStatus's own doc comment for why this is one call rather than
/// three separate ones.
/// </summary>
public interface IPrinterStatusReader
{
    Task<PrinterStatus> ReadStatusAsync(PrinterDevice device, bool allowBleFallback = true, CancellationToken cancellationToken = default);
}
