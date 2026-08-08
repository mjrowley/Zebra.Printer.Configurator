using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Ensures the printer's optional "PDF Direct" emulation (Zebra's apl.enable "pdf" virtual device)
/// is loaded and enabled - installing the bundled virtual device file first if it isn't already.
/// </summary>
public interface IPdfDirectService
{
    Task EnsureEnabledAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
