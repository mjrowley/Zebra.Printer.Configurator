using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class CheckConfigurationPanelTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IPrinterConfigurationReader _configurationReader = Substitute.For<IPrinterConfigurationReader>();

    public CheckConfigurationPanelTests()
    {
        Services.AddSingleton(_configurationReader);
    }

    [Fact]
    public void InitialRender_ShowsButtonOnlyAndNoResults()
    {
        var cut = Render<CheckConfigurationPanel>(p => p.Add(c => c.Device, Device));

        Assert.NotNull(cut.Find("[data-testid='check-configuration-button']"));
        Assert.Empty(cut.FindAll("[data-testid='check-configuration-results']"));
        Assert.Empty(cut.FindAll("[data-testid='check-configuration-error']"));
    }

    [Fact]
    public void ClickingButton_ShowsRetrievedValues()
    {
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.essid", "Warehouse-WiFi"), new PrinterConfigurationValue("wlan.state", "CONNECTED")]);
        var cut = Render<CheckConfigurationPanel>(p => p.Add(c => c.Device, Device));

        cut.Find("[data-testid='check-configuration-button']").Click();

        cut.WaitForAssertion(() =>
        {
            var results = cut.Find("[data-testid='check-configuration-results']");
            Assert.Contains("wlan.essid", results.TextContent);
            Assert.Contains("Warehouse-WiFi", results.TextContent);
            Assert.Contains("wlan.state", results.TextContent);
            Assert.Contains("CONNECTED", results.TextContent);
        });
    }

    [Fact]
    public void WhenReadFails_ShowsErrorAndNoResults()
    {
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<PrinterConfigurationValue>>(new InvalidOperationException("simulated failure")));
        var cut = Render<CheckConfigurationPanel>(p => p.Add(c => c.Device, Device));

        cut.Find("[data-testid='check-configuration-button']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='check-configuration-error']")));
        Assert.Empty(cut.FindAll("[data-testid='check-configuration-results']"));
    }

    [Fact]
    public void WhenDisabled_ButtonHasDisabledAttribute()
    {
        var cut = Render<CheckConfigurationPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.Disabled, true));

        Assert.True(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
    }
}
