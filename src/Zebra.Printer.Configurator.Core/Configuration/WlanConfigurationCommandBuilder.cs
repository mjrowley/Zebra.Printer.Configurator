using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Configuration;

/// <summary>
/// Builds the ordered SGD SET key/value pairs to push a <see cref="WlanConfiguration"/> to a
/// printer. Pure and Zebra-SDK-independent so command sequencing is unit-testable without a real
/// BluetoothConnection; the Infrastructure.Android service just replays these through SGD.SET.
///
/// Grounded in Zebra's own SGD reference (docs.zebra.com ZPL Programming Guide, SGD Wireless
/// Commands) rather than assumption, after on-device testing showed a printer accepting these
/// commands without error but never actually applying them: wlan.password isn't a real SGD key
/// (the WPA/WPA2 passphrase key is wlan.wpa.psk, and wlan.security must be set to a matching mode
/// for the printer to use it at all), and static IP values are accepted but ignored unless
/// wlan.ip.protocol is explicitly set to "permanent" - Zebra's docs state this outright: "For a set
/// IP address to take effect, the IP protocol must be set to permanent and the print server must be
/// reset."
/// </summary>
public static class WlanConfigurationCommandBuilder
{
    public static IReadOnlyList<(string Key, string Value)> BuildSetCommands(WlanConfiguration configuration)
    {
        var commands = new List<(string Key, string Value)>
        {
            // Neither the factory-default fallback address nor DHCP/BOOTP addressing should be in
            // play once a static IP is configured - both must be turned off/switched to "permanent"
            // for the wlan.ip.* values below to actually take effect, not just be accepted.
            ("wlan.ip.default_addr_enable", "off"),
            ("wlan.ip.protocol", "permanent"),
        };

        if (string.IsNullOrEmpty(configuration.Password))
        {
            // WifiPasswordValidator treats an empty password as a deliberately open network.
            commands.Add(("wlan.security", "open"));
        }
        else
        {
            commands.Add(("wlan.security", "wpa2-psk"));
            commands.Add(("wlan.wpa.psk", configuration.Password));
        }

        commands.Add(("wlan.ssid", configuration.Ssid));
        commands.Add(("wlan.ip.addr", configuration.StaticIpAddress));
        commands.Add(("wlan.ip.netmask", configuration.Netmask));
        commands.Add(("wlan.ip.gateway", configuration.Gateway));

        return commands;
    }
}
