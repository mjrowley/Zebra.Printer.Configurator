using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Compares a newly-connected printer's actual Link-OS/firmware versions against the bundled
/// baseline for its model (see Core.Firmware.FirmwareBundleCatalog). Run automatically right after
/// Bluetooth pairing succeeds, before "Configure Printer" becomes available.
/// </summary>
public interface IPrinterVersionCheckService
{
    Task<PrinterVersionCheckResult> CheckAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
