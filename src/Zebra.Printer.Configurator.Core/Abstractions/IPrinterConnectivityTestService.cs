using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Polls the printer's new static IP after restart to confirm it rejoined the target WiFi network.
/// </summary>
public interface IPrinterConnectivityTestService
{
    Task<ConnectionTestResult> TestConnectionAsync(WlanConfiguration configuration, CancellationToken cancellationToken = default);
}
