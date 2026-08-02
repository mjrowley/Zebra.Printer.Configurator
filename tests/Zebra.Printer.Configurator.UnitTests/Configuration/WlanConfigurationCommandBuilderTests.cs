using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UnitTests.Configuration;

public class WlanConfigurationCommandBuilderTests
{
    private static readonly WlanConfiguration Configuration = new()
    {
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    [Fact]
    public void BuildSetCommands_PutsDefaultAddrEnableOffFirst()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(Configuration);

        Assert.Equal(("wlan.ip.default_addr_enable", "off"), commands[0]);
    }

    [Fact]
    public void BuildSetCommands_IncludesEveryConfiguredField()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(Configuration);

        Assert.Contains(("wlan.ssid", Configuration.Ssid), commands);
        Assert.Contains(("wlan.password", Configuration.Password), commands);
        Assert.Contains(("wlan.ip.addr", Configuration.StaticIpAddress), commands);
        Assert.Contains(("wlan.ip.netmask", Configuration.Netmask), commands);
        Assert.Contains(("wlan.ip.gateway", Configuration.Gateway), commands);
    }

    [Fact]
    public void BuildSetCommands_SetsStaticIpFieldsAfterDefaultAddrEnable()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(Configuration);

        var defaultAddrEnableIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.default_addr_enable");
        var ipAddrIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.addr");
        var netmaskIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.netmask");
        var gatewayIndex = commands.ToList().FindIndex(c => c.Key == "wlan.ip.gateway");

        Assert.True(defaultAddrEnableIndex < ipAddrIndex);
        Assert.True(defaultAddrEnableIndex < netmaskIndex);
        Assert.True(defaultAddrEnableIndex < gatewayIndex);
    }

    [Fact]
    public void BuildSetCommands_ReturnsExactlySixCommands()
    {
        var commands = WlanConfigurationCommandBuilder.BuildSetCommands(Configuration);

        Assert.Equal(6, commands.Count);
    }
}
