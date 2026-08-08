using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Starts a firmware update as a platform-appropriate long-running background operation (an Android
/// foreground service) that survives the screen locking or the app being backgrounded - a plain
/// awaited Task tied to a Razor component's lifecycle does not. Returns once the operation has been
/// asked to start, not once it finishes; progress/outcome are tracked separately via
/// FirmwareUpdateStatusMonitor.
/// </summary>
public interface IFirmwareUpdateLauncher
{
    Task StartAsync(PrinterDevice device, FirmwareBundle bundle, string wifiIpAddress, CancellationToken cancellationToken = default);
}
