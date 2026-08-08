using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Sends a bundled firmware file to the printer via the Zebra Link-OS SDK's FirmwareUpdaterLinkOs.
/// Requires WiFi (a 41MB Bluetooth Classic transfer would take several minutes) - the caller passes
/// the printer's already-confirmed-reachable WiFi IP explicitly, rather than this reading it back out
/// of shared connection-mode state, so a stale/not-yet-set value can't silently produce a confusing
/// low-level connection error.
/// </summary>
public interface IPrinterFirmwareUpdateService
{
    Task UpdateFirmwareAsync(PrinterDevice device, FirmwareBundle bundle, string wifiIpAddress, IProgress<FirmwareUpdateProgress> progress, CancellationToken cancellationToken = default);
}
