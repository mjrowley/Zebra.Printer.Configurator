using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Reads the printer's current WLAN-related SGD settings over Bluetooth, for on-demand display in
/// the UI (the "Check Configuration" action) - independent of the automatic post-restart
/// connectivity test, and usable any time a printer is reachable over Bluetooth.
/// </summary>
public interface IPrinterConfigurationReader
{
    Task<IReadOnlyList<PrinterConfigurationValue>> ReadConfigurationAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
