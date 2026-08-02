using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Resets a printer to its factory default configuration over the Bluetooth connection established
/// from the NFC tap. Irreversible on the printer side, and since Bluetooth bonding/friendly-name
/// settings are among the values restored to their factory defaults, it may also break the current
/// Bluetooth pairing - the caller should warn the user before invoking this.
/// </summary>
public interface IPrinterFactoryResetService
{
    Task ResetToFactoryDefaultsAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
