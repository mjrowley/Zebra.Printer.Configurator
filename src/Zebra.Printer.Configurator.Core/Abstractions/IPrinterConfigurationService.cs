using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Applies WLAN settings to a printer over the Bluetooth connection established from the NFC tap.
/// </summary>
public interface IPrinterConfigurationService
{
    Task ApplyAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default);
}
