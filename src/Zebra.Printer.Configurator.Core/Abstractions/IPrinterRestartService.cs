using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Issues the printer restart command once WLAN configuration has been applied.
/// </summary>
public interface IPrinterRestartService
{
    Task RestartAsync(PrinterDevice device, IPrinterConnectionSession session, CancellationToken cancellationToken = default);
}
