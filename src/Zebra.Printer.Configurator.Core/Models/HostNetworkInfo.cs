namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// The host device's current WiFi connection details, read via IHostNetworkInfoService and
/// inherited into a <see cref="WlanConfiguration"/>'s netmask/gateway.
/// </summary>
public sealed record HostNetworkInfo
{
    public required string Netmask { get; init; }

    public required string Gateway { get; init; }

    public string? Ssid { get; init; }
}
