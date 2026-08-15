using Zebra.Printer.Configurator.Core.Configuration;

namespace Zebra.Printer.Configurator.UnitTests.Configuration;

public class WlanDiagnosticKeysTests
{
    [Fact]
    public void All_StartsWithDeviceFriendlyName()
    {
        Assert.Equal("device.friendly_name", WlanDiagnosticKeys.All[0]);
    }

    [Fact]
    public void All_StillContainsEveryOriginalKey()
    {
        Assert.Equal(
            [
                "device.friendly_name",
                "wlan.enable",
                "wlan.security",
                "wlan.essid",
                "wlan.wpa.psk",
                "wlan.ip.protocol",
                "wlan.ip.default_addr_enable",
                "wlan.ip.addr",
                "wlan.ip.netmask",
                "wlan.ip.gateway",
                "wlan.state",
                "apl.enable",
                "media.printmode",
                "ezpl.media_type",
                "ezpl.print_method",
                "ezpl.print_width",
                "ezpl.label_length_max",
            ],
            WlanDiagnosticKeys.All);
    }
}
