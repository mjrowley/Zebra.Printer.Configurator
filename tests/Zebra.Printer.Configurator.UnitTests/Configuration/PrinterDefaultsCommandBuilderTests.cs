using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UnitTests.Configuration;

public class PrinterDefaultsCommandBuilderTests
{
    private static readonly WlanConfiguration Configuration = new()
    {
        PrinterName = "ZD421",
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        IpAddressMode = WlanIpAddressMode.Static,
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    [Fact]
    public void BuildSetCommands_IncludesEveryFixedDefault()
    {
        var commands = PrinterDefaultsCommandBuilder.BuildSetCommands("ZD421");

        Assert.Contains(("media.printmode", "tear off"), commands);
        Assert.Contains(("device.friendly_name", "ZD421"), commands);
        Assert.Contains(("ezpl.media_type", "gap/notch"), commands);
        Assert.Contains(("ezpl.print_method", "direct thermal"), commands);
        Assert.Contains(("ezpl.print_width", "799"), commands);
        Assert.Contains(("ezpl.label_length_max", "7"), commands);
        Assert.Contains(("zpl.left_position", "-10"), commands);
        Assert.Contains(("apl.settings", "scale-to-fit"), commands);
    }

    [Fact]
    public void BuildSetCommands_ReturnsExpectedCount()
    {
        var commands = PrinterDefaultsCommandBuilder.BuildSetCommands("ZD421");

        Assert.Equal(8, commands.Count);
    }

    [Fact]
    public void BuildSetCommands_UsesTheGivenPrinterNameForDeviceFriendlyName()
    {
        var commands = PrinterDefaultsCommandBuilder.BuildSetCommands("Warehouse Printer 3");

        Assert.Contains(("device.friendly_name", "Warehouse Printer 3"), commands);
        Assert.DoesNotContain(commands, c => c.Key == "device.friendly_name" && c.Value == "ZD421");
    }

    [Fact]
    public void BuildExpectedDiagnosticValues_IncludesFixedDefaultsWlanSettingsAndPdfDirect()
    {
        var expected = PrinterDefaultsCommandBuilder.BuildExpectedDiagnosticValues(Configuration.PrinterName, Configuration);

        // Fixed defaults
        Assert.Equal("799", expected["ezpl.print_width"]);
        Assert.Equal("-10", expected["zpl.left_position"]);
        Assert.Equal("scale-to-fit", expected["apl.settings"]);
        Assert.Equal(Configuration.PrinterName, expected["device.friendly_name"]);

        // WLAN settings for this pairing attempt
        Assert.Equal("on", expected["wlan.enable"]);
        Assert.Equal(Configuration.Ssid, expected["wlan.essid"]);
        Assert.Equal(Configuration.StaticIpAddress, expected["wlan.ip.addr"]);

        // PDF Direct - set via LinkOsPdfDirectService's own flow, not BuildSetCommands, but still
        // something this app expects
        Assert.Equal(PrinterDefaultsCommandBuilder.PdfEnabledValue, expected["apl.enable"]);
    }

    [Fact]
    public void BuildExpectedDiagnosticValues_OmitsWpaPsk_ForAnOpenNetwork()
    {
        var openConfiguration = Configuration with { Password = "" };

        var expected = PrinterDefaultsCommandBuilder.BuildExpectedDiagnosticValues(openConfiguration.PrinterName, openConfiguration);

        Assert.Equal("none", expected["wlan.security"]);
        Assert.False(expected.ContainsKey("wlan.wpa.psk"));
    }

    [Fact]
    public void BuildFixedDiagnosticDefaults_IncludesFixedDefaultsAndPdfDirect_WithoutRequiringAConfiguration()
    {
        var expected = PrinterDefaultsCommandBuilder.BuildFixedDiagnosticDefaults();

        Assert.Equal("799", expected["ezpl.print_width"]);
        Assert.Equal("-10", expected["zpl.left_position"]);
        Assert.Equal("scale-to-fit", expected["apl.settings"]);
        Assert.Equal("tear off", expected["media.printmode"]);
        Assert.Equal(PrinterDefaultsCommandBuilder.PdfEnabledValue, expected["apl.enable"]);
    }

    [Fact]
    public void BuildFixedDiagnosticDefaults_OmitsDeviceFriendlyNameAndWlanKeys()
    {
        var expected = PrinterDefaultsCommandBuilder.BuildFixedDiagnosticDefaults();

        Assert.False(expected.ContainsKey("device.friendly_name"));
        Assert.False(expected.ContainsKey("wlan.essid"));
        Assert.False(expected.ContainsKey("wlan.ip.addr"));
    }
}
