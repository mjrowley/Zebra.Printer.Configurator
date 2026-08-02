using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UnitTests.Configuration;

public class WlanConfigurationCommandBuilderTests
{
    private static readonly WlanConfiguration SecuredConfiguration = new()
    {
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    private static readonly WlanConfiguration OpenConfiguration = SecuredConfiguration with { Password = "" };

    [Fact]
    public void BuildSetCommands_PutsDefaultAddrEnableOffFirst()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        Assert.Equal(("wlan.ip.default_addr_enable", "off"), commands[0]);
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
    public void BuildSetCommands_ForSecuredNetwork_SetsWpa2PskSecurityAndPassphrase()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        Assert.Contains(("wlan.security", "wpa2-psk"), commands);
        Assert.Contains(("wlan.wpa.psk", SecuredConfiguration.Password), commands);
        Assert.DoesNotContain(commands, c => c.Key == "wlan.password");
    }

    [Fact]
    public void BuildSetCommands_ForOpenNetwork_SetsOpenSecurityAndOmitsPsk()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(OpenConfiguration);

        Assert.Contains(("wlan.security", "open"), commands);
        Assert.DoesNotContain(commands, c => c.Key == "wlan.wpa.psk");
    }

    [Fact]
    public void BuildSetCommands_IncludesEveryConfiguredField()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        Assert.Contains(("wlan.ssid", SecuredConfiguration.Ssid), commands);
        Assert.Contains(("wlan.ip.addr", SecuredConfiguration.StaticIpAddress), commands);
        Assert.Contains(("wlan.ip.netmask", SecuredConfiguration.Netmask), commands);
        Assert.Contains(("wlan.ip.gateway", SecuredConfiguration.Gateway), commands);
    }

    [Fact]
    public void BuildSetCommands_SetsStaticIpFieldsAfterDefaultAddrEnableAndIpProtocol()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        var defaultAddrEnableIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.default_addr_enable");
        var ipProtocolIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.protocol");
        var ipAddrIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.addr");
        var netmaskIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.netmask");
        var gatewayIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.gateway");

        Assert.True(defaultAddrEnableIndex < ipAddrIndex);
        Assert.True(ipProtocolIndex < ipAddrIndex);
        Assert.True(defaultAddrEnableIndex < netmaskIndex);
        Assert.True(defaultAddrEnableIndex < gatewayIndex);
    }

    [Fact]
    public void BuildSetCommands_ReturnsExpectedCountForSecuredNetwork()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(SecuredConfiguration);

        // default_addr_enable, ip.protocol, security, wpa.psk, ssid, ip.addr, netmask, gateway
        Assert.Equal(8, commands.Count);
    }

    [Fact]
    public void BuildSetCommands_ReturnsExpectedCountForOpenNetwork()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(OpenConfiguration);

        // Same as secured, minus wlan.wpa.psk
        Assert.Equal(7, commands.Count);
    }
}
