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
    public void All_IsAlphabeticalAfterDeviceFriendlyName()
    {
        var rest = WlanDiagnosticKeys.All.Skip(1).ToList();
        var sorted = rest.OrderBy(key => key, StringComparer.Ordinal).ToList();

        Assert.Equal(sorted, rest);
    }

    [Fact]
    public void All_StillContainsEveryOriginalKey()
    {
        Assert.Equal(
            [
                "device.friendly_name",
                "apl.enable",
                "apl.settings",
                "ezpl.label_length_max",
                "ezpl.media_type",
                "ezpl.print_method",
                "ezpl.print_width",
                "media.printmode",
                "wlan.enable",
                "wlan.essid",
                "wlan.ip.addr",
                "wlan.ip.default_addr_enable",
                "wlan.ip.gateway",
                "wlan.ip.netmask",
                "wlan.ip.protocol",
                "wlan.security",
                "wlan.state",
                "wlan.wpa.psk",
                "zpl.left_position",
            ],
            WlanDiagnosticKeys.All);
    }
}
