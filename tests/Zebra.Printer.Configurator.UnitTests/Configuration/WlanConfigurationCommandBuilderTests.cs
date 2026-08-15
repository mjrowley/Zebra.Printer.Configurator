using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UnitTests.Configuration;

public class WlanConfigurationCommandBuilderTests
{
    private static readonly WlanConfiguration SecuredConfiguration = new()
    {
        PrinterName = "ZD421",
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    private static readonly WlanConfiguration OpenConfiguration = SecuredConfiguration with { Password = "" };

    [Fact]
    public void BuildSetCommands_EnablesWlanRadioFirst()
    {
        // Confirmed by reading the printer's own settings back over Bluetooth after a failed
        // connect: wlan.ssid/wlan.security/wlan.wpa.psk were still their untouched defaults even
        // though wlan.ip.* had all applied correctly - the radio-specific settings are silently
        // ignored while the radio is off, so it must be enabled before them, not after.
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        Assert.Equal(("wlan.enable", "on"), commands[0]);
    }

    [Fact]
    public void BuildSetCommands_SetsIpProtocolToPermanent()
    {
        // Confirmed via Zebra's own SGD docs: "For a set IP address to take effect, the IP
        // protocol must be set to permanent" - without this the static IP fields are accepted but
        // never actually applied (the printer stays on DHCP).
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        Assert.Contains(("wlan.ip.protocol", "permanent"), commands);
    }

    [Fact]
    public void BuildSetCommands_ForSecuredNetwork_SetsWpaPskSecurityAndDerivedHexPassphrase()
    {
        // "wpa2-psk" is not a value wlan.security recognizes at all (confirmed against Zebra's SGD
        // Wireless Commands reference: the documented values are numeric codes 1-15 or their exact
        // name aliases, e.g. "9"/"wpa psk") - it was silently rejected and the printer stayed on its
        // default ("none") even though the command "succeeded". Likewise wlan.wpa.psk's documented
        // setvar value is 64 hexadecimal digits, not the raw ASCII passphrase.
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        Assert.Contains(("wlan.security", "wpa psk"), commands);
        Assert.Contains(
            ("wlan.wpa.psk", WpaPskDeriver.DeriveHexPsk(SecuredConfiguration.Ssid, SecuredConfiguration.Password)),
            commands);
        Assert.DoesNotContain(commands, c => c.Key == "wlan.password");
    }

    [Fact]
    public void BuildSetCommands_ForOpenNetwork_SetsNoneSecurityAndOmitsPsk()
    {
        // The printer's own reported default/native value for an unsecured network is "none",
        // not "open" - "open" was likely rejected as an unrecognized value on this firmware.
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(OpenConfiguration);

        Assert.Contains(("wlan.security", "none"), commands);
        Assert.DoesNotContain(commands, c => c.Key == "wlan.wpa.psk");
    }

    [Fact]
    public void BuildSetCommands_IncludesEveryConfiguredField()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        Assert.Contains(("wlan.essid", SecuredConfiguration.Ssid), commands);
        Assert.Contains(("wlan.ip.addr", SecuredConfiguration.StaticIpAddress), commands);
        Assert.Contains(("wlan.ip.netmask", SecuredConfiguration.Netmask), commands);
        Assert.Contains(("wlan.ip.gateway", SecuredConfiguration.Gateway), commands);
    }

    [Fact]
    public void BuildSetCommands_SetsStaticIpFieldsAfterEnableAndDefaultAddrEnableAndIpProtocol()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        var enableIndex = commands.ToList().FindIndex(c => c.Key == "wlan.enable");
        var defaultAddrEnableIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.default_addr_enable");
        var ipProtocolIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.protocol");
        var ipAddrIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.addr");
        var netmaskIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.netmask");
        var gatewayIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.gateway");

        Assert.True(enableIndex < defaultAddrEnableIndex);
        Assert.True(defaultAddrEnableIndex < ipAddrIndex);
        Assert.True(ipProtocolIndex < ipAddrIndex);
        Assert.True(defaultAddrEnableIndex < netmaskIndex);
        Assert.True(defaultAddrEnableIndex < gatewayIndex);
    }

    [Fact]
    public void BuildSetCommands_SetsWlanEnableBeforeRadioSpecificSettings()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        var enableIndex = commands.ToList().FindIndex(c => c.Key == "wlan.enable");
        var securityIndex = commands.ToList().FindIndex(c => c.Key == "wlan.security");
        var pskIndex = commands.ToList().FindIndex(c => c.Key == "wlan.wpa.psk");
        var ssidIndex = commands.ToList().FindIndex(c => c.Key == "wlan.essid");

        Assert.True(enableIndex < securityIndex);
        Assert.True(enableIndex < pskIndex);
        Assert.True(enableIndex < ssidIndex);
    }

    [Fact]
    public void BuildSetCommands_ReturnsExpectedCountForSecuredNetwork()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        // enable, default_addr_enable, ip.protocol, security, wpa.psk, ssid, ip.addr, netmask, gateway
        Assert.Equal(9, commands.Count);
    }

    [Fact]
    public void BuildSetCommands_ReturnsExpectedCountForOpenNetwork()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(OpenConfiguration);

        // Same as secured, minus wlan.wpa.psk
        Assert.Equal(8, commands.Count);
    }
}
