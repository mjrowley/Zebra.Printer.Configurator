using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Polls the printer's new static IP after restart to confirm it rejoined the target WiFi network.
/// If it doesn't appear within the timeout, implementations may reconnect via Bluetooth (using
/// <paramref name="device"/>) to read back and log the printer's actual WLAN settings as a
/// diagnostic, since that's the most direct way to see which of the applied settings didn't stick.
/// </summary>
public interface IPrinterConnectivityTestService
{
    Task<ConnectionTestResult> TestConnectionAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default);
}
