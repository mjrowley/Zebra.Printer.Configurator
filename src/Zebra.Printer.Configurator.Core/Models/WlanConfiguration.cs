namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// WLAN settings to push to a printer. <see cref="Ssid"/>, <see cref="Password"/>, and
/// <see cref="StaticIpAddress"/> are user-entered; <see cref="Netmask"/> and <see cref="Gateway"/>
/// are inherited from the host device's current WiFi connection (see IHostNetworkInfoService),
/// since the phone running this app is assumed to already be on the target network.
/// </summary>
public sealed record WlanConfiguration
{
    public required string Ssid { get; init; }

    public required string Password { get; init; }

    public required string StaticIpAddress { get; init; }

    public required string Netmask { get; init; }

    public required string Gateway { get; init; }
}
