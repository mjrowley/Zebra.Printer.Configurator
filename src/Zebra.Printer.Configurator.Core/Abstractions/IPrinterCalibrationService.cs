using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Runs the printer's own media calibration routine (the programmatic equivalent of holding
/// PAUSE + CANCEL on the printer itself) - feeds and measures a few labels to (re)detect media
/// type/length and set gap/black-mark sensing levels correctly for whatever's currently loaded.
/// Needed whenever the printer isn't sensing label gaps ("web") correctly - e.g. after loading a
/// different media stock - separate from the fixed printer defaults PrinterDefaultsCommandBuilder
/// applies during Configure/Reconfigure Printer, which don't touch sensor calibration at all.
/// </summary>
public interface IPrinterCalibrationService
{
    Task CalibrateAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
