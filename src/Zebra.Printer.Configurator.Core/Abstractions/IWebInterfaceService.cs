using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Reads and toggles the printer's web interface (ip.https.enable / ip.http.enable), and restarts
/// it so a toggle actually takes effect. RestartPrinterAsync is deliberately not named
/// RestartAsync/reusing IPrinterRestartService - that interface's RestartAsync takes a shared
/// IPrinterConnectionSession, a different shape for a different use (the main configure workflow,
/// where several steps share one connection); this is a standalone action a user can trigger
/// independently, any time after a toggle, not bundled into a session.
/// </summary>
public interface IWebInterfaceService
{
    Task<WebInterfaceState> ReadStateAsync(PrinterDevice device, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(PrinterDevice device, bool enabled, CancellationToken cancellationToken = default);

    Task RestartPrinterAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
