namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// WLAN settings (plus the printer's own <see cref="PrinterName"/>) to push to a printer, all
/// entered on the same Configure form. <see cref="PrinterName"/>, <see cref="Ssid"/>,
/// <see cref="Password"/>, <see cref="IpAddressMode"/>, and (when <see cref="IpAddressMode"/> is
/// <see cref="WlanIpAddressMode.Static"/>) <see cref="StaticIpAddress"/> are user-entered;
/// <see cref="Netmask"/> and <see cref="Gateway"/> are inherited from the host device's current WiFi
/// connection (see IHostNetworkInfoService), since the phone running this app is assumed to already
/// be on the target network.
/// </summary>
public sealed record WlanConfiguration
{
    public required string PrinterName { get; init; }

    public required string Ssid { get; init; }

    public required string Password { get; init; }

    public required WlanIpAddressMode IpAddressMode { get; init; }

    // Meaningless when IpAddressMode is Dhcp - the printer picks its own address - so left blank
    // rather than required in that case.
    public string StaticIpAddress { get; init; } = string.Empty;

    public required string Netmask { get; init; }

    public required string Gateway { get; init; }
}
