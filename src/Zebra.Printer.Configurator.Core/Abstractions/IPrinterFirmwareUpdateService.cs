using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Sends a bundled firmware file to the printer via the Zebra Link-OS SDK's FirmwareUpdaterLinkOs.
/// Requires WiFi (a 41MB Bluetooth Classic transfer would take several minutes) - callers are
/// expected to have already confirmed/switched to a WiFi connection before calling this.
/// </summary>
public interface IPrinterFirmwareUpdateService
{
    Task UpdateFirmwareAsync(PrinterDevice device, FirmwareBundle bundle, IProgress<FirmwareUpdateProgress> progress, CancellationToken cancellationToken = default);
}
