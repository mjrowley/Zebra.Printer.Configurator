namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// The host device's current WiFi connection details, read via IHostNetworkInfoService. Netmask and
/// Gateway are inherited directly into a <see cref="WlanConfiguration"/>; HostIpAddress is only used
/// to pre-fill the printer's static IP form field, on the assumption the printer will join the same
/// subnet the phone is already on.
/// </summary>
public sealed record HostNetworkInfo
{
    public required string HostIpAddress { get; init; }

    public required string Netmask { get; init; }

    public required string Gateway { get; init; }

    public string? Ssid { get; init; }
}
