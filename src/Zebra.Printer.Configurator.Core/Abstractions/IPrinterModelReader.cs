using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Reads a printer's reported model name (the "device.product_name" SGD key) over whichever
/// transport is currently active - used to default the "Printer Name" field on the Configure page
/// to the printer's actual model instead of a hardcoded assumption.
/// </summary>
public interface IPrinterModelReader
{
    Task<string?> ReadModelNameAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
