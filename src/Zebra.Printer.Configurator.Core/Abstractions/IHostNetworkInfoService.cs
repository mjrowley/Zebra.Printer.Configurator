using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Reads the host device's current WiFi connection so its netmask/gateway can be inherited into
/// the printer's static IP configuration. Returns null if the host isn't currently on WiFi.
/// </summary>
public interface IHostNetworkInfoService
{
    Task<HostNetworkInfo?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
