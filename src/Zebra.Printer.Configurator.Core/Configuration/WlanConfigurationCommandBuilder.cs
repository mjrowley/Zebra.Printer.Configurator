using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Configuration;

/// <summary>
/// Builds the ordered SGD SET key/value pairs to push a <see cref="WlanConfiguration"/> to a
/// printer. Pure and Zebra-SDK-independent so command sequencing is unit-testable without a real
/// BluetoothConnection; the Infrastructure.Android service just replays these through SGD.SET.
///
/// Grounded in Zebra's own SGD reference (docs.zebra.com ZPL Programming Guide, SGD Wireless
/// Commands) and in reading the printer's own settings back over Bluetooth after a failed connect,
/// rather than continued assumption: wlan.password isn't a real SGD key (the WPA/WPA2 passphrase
/// key is wlan.wpa.psk), static IP values need wlan.ip.protocol=permanent to actually take effect
/// (not just be accepted), wlan.enable must be turned on BEFORE the radio-specific settings
/// (security/PSK/network name) are sent since the printer silently ignores those while the radio is
/// off, and the printer's own reported default for an unsecured network is "none", not "open".
///
/// wlan.ssid (used until this point) is not a real SGD command either - Zebra's own troubleshooting
/// docs confirm the actual key is wlan.essid: "If the wireless network name isn't specified, users
/// must set it using the command: `! U1 setvar "wlan.essid" "value"`". This is the same
/// silently-accepted-but-inert failure mode as wlan.password/device.restart before it: SGD.SET on an
/// unrecognized key just stores it under that name without connecting it to anything the radio
/// reads, so reading wlan.ssid back afterwards "confirmed" a value that was never actually driving
/// the radio's association at all - it explains wlan.state staying blank even after every other
/// setting (including the bogus wlan.ssid itself) read back exactly as sent.
///
/// wlan.security and wlan.wpa.psk had the same class of problem, confirmed against Zebra's SGD
/// Wireless Commands reference (Programming Guide for ZPL II/ZBI 2/SGD, "wlan.security"/
/// "wlan.wpa.psk" sections): wlan.security only accepts a documented set of numeric codes or their
/// exact name aliases (e.g. "1"/"none", "9"/"wpa psk") - "wpa2-psk" is neither, so it was silently
/// rejected and the printer stayed on its default ("1"/"none") even for a secured network. And
/// wlan.wpa.psk's documented setvar value is "64 hexadecimal digits", not an ASCII passphrase - the
/// printer accepted the raw password string under that key but it was never a valid PSK the radio
/// could authenticate with. The docs also note getvar on wlan.wpa.psk always prints a single "*"
/// "for protection", regardless of what's stored - so a verification pass must not expect the sent
/// value to be echoed back for that key alone.
/// </summary>
public static class WlanConfigurationCommandBuilder
{
    public static IReadOnlyList<(string Key, string Value)> BuildSetCommands(WlanConfiguration configuration)
    {
        var commands = new List<(string Key, string Value)>
        {
            // Radio-specific settings (security/PSK/SSID) below are silently ignored by the
            // printer while the radio is off, so it must be enabled first, not last.
            ("wlan.enable", "on"),

            // Neither the factory-default fallback address nor DHCP/BOOTP addressing should be in
            // play once a static IP is configured - both must be turned off/switched to "permanent"
            // for the wlan.ip.* values below to actually take effect, not just be accepted.
            ("wlan.ip.default_addr_enable", "off"),
            ("wlan.ip.protocol", "permanent"),
        };

        if (string.IsNullOrEmpty(configuration.Password))
        {
            // WifiPasswordValidator treats an empty password as a deliberately open network.
            // The printer's own reported default/native value for this is "none", not "open".
            commands.Add(("wlan.security", "none"));
        }
        else
        {
            // "wpa psk" (value 9) covers both WPA and WPA2 PSK - Zebra's docs note that configuring
            // for WPA also allows the printer to associate on WPA2 networks.
            commands.Add(("wlan.security", "wpa psk"));
            commands.Add(("wlan.wpa.psk", WpaPskDeriver.DeriveHexPsk(configuration.Ssid, configuration.Password)));
        }

        commands.Add(("wlan.essid", configuration.Ssid));
        commands.Add(("wlan.ip.addr", configuration.StaticIpAddress));
        commands.Add(("wlan.ip.netmask", configuration.Netmask));
        commands.Add(("wlan.ip.gateway", configuration.Gateway));

        return commands;
    }
}
