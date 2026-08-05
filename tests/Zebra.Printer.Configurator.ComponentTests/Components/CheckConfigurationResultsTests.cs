using Bunit;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class CheckConfigurationResultsTests : BunitContext
{
    [Fact]
    public void InitialRender_WithFreshState_ShowsNothing()
    {
        var cut = Render<CheckConfigurationResults>(p => p.Add(c => c.State, new CheckConfigurationState()));

        Assert.Empty(cut.FindAll("[data-testid='check-configuration-results']"));
        Assert.Empty(cut.FindAll("[data-testid='check-configuration-error']"));
    }

    [Fact]
    public void WhenStateGetsResults_ShowsTable()
    {
        var state = new CheckConfigurationState();
        var cut = Render<CheckConfigurationResults>(p => p.Add(c => c.State, state));

        state.SetResults([new PrinterConfigurationValue("wlan.essid", "Warehouse-WiFi"), new PrinterConfigurationValue("wlan.state", "CONNECTED")]);

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
    public void WhenStateGetsError_ShowsErrorAndNoResults()
    {
        var state = new CheckConfigurationState();
        var cut = Render<CheckConfigurationResults>(p => p.Add(c => c.State, state));

        state.SetError("Could not check configuration: simulated failure");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='check-configuration-error']")));
        Assert.Empty(cut.FindAll("[data-testid='check-configuration-results']"));
    }
}
