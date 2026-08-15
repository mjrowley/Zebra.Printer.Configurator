using Zebra.Printer.Configurator.Core.Configuration;

namespace Zebra.Printer.Configurator.UnitTests.Configuration;

public class PrinterDefaultsCommandBuilderTests
{
    [Fact]
    public void BuildSetCommands_IncludesEveryFixedDefault()
    {
        var commands = PrinterDefaultsCommandBuilder.BuildSetCommands("ZD421");

        Assert.Contains(("media.printmode", "tear off"), commands);
        Assert.Contains(("device.friendly_name", "ZD421"), commands);
        Assert.Contains(("ezpl.media_type", "gap/notch"), commands);
        Assert.Contains(("ezpl.print_method", "direct thermal"), commands);
        Assert.Contains(("ezpl.print_width", "812"), commands);
        Assert.Contains(("ezpl.label_length_max", "7"), commands);
    }

    [Fact]
    public void BuildSetCommands_ReturnsExpectedCount()
    {
        var commands = PrinterDefaultsCommandBuilder.BuildSetCommands("ZD421");

        Assert.Equal(6, commands.Count);
    }

    [Fact]
    public void BuildSetCommands_UsesTheGivenPrinterNameForDeviceFriendlyName()
    {
        var commands = PrinterDefaultsCommandBuilder.BuildSetCommands("Warehouse Printer 3");

        Assert.Contains(("device.friendly_name", "Warehouse Printer 3"), commands);
        Assert.DoesNotContain(commands, c => c.Key == "device.friendly_name" && c.Value == "ZD421");
    }
}
