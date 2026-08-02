using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Configuration;

/// <summary>
/// Builds the ordered SGD SET key/value pairs to push a <see cref="WlanConfiguration"/> to a
/// printer. Pure and Zebra-SDK-independent so command sequencing is unit-testable without a real
/// BluetoothConnection; the Infrastructure.Android service just replays these through SGD.SET.
/// </summary>
public static class WlanConfigurationCommandBuilder
{
    /// <summary>
    /// wlan.ip.default_addr_enable must be switched off before the static IP fields are set,
    /// otherwise the printer stays in DHCP mode and ignores them - hence it comes first.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value)> BuildSetCommands(WlanConfiguration configuration) =>
    [
        ("wlan.ip.default_addr_enable", "off"),
        ("wlan.ssid", configuration.Ssid),
        ("wlan.password", configuration.Password),
        ("wlan.ip.addr", configuration.StaticIpAddress),
        ("wlan.ip.netmask", configuration.Netmask),
        ("wlan.ip.gateway", configuration.Gateway),
    ];
}
