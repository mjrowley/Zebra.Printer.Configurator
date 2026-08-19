using Bunit;
using Zebra.Printer.Configurator.Core.Configuration;
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

        state.SetResults([new PrinterConfigurationValue("wlan.essid", "Warehouse-WiFi"), new PrinterConfigurationValue("wlan.security", "wpa psk")]);

        cut.WaitForAssertion(() =>
        {
            var results = cut.Find("[data-testid='check-configuration-results']");
            Assert.Contains("wlan.essid", results.TextContent);
            Assert.Contains("Warehouse-WiFi", results.TextContent);
            Assert.Contains("wlan.security", results.TextContent);
            Assert.Contains("wpa psk", results.TextContent);
        });
    }

    [Fact]
    public void HiddenKeys_AreNotRendered()
    {
        // wlan.ip.default_addr_enable is an obscure fallback setting nobody reading this raw list
        // needs, and wlan.state duplicates what PrinterInfo.razor's "About Printer" -> Connectivity
        // section already shows more legibly - both are deliberately filtered out here.
        var state = new CheckConfigurationState();
        var cut = Render<CheckConfigurationResults>(p => p.Add(c => c.State, state));

        state.SetResults([
            new PrinterConfigurationValue("wlan.essid", "Warehouse-WiFi"),
            new PrinterConfigurationValue("wlan.ip.default_addr_enable", "off"),
            new PrinterConfigurationValue("wlan.state", "CONNECTED"),
        ]);

        cut.WaitForAssertion(() =>
        {
            var results = cut.Find("[data-testid='check-configuration-results']");
            Assert.Contains("wlan.essid", results.TextContent);
            Assert.DoesNotContain("wlan.ip.default_addr_enable", results.TextContent);
            Assert.DoesNotContain("wlan.state", results.TextContent);
        });
    }

    [Fact]
    public void MatchingValue_IsRenderedWithTheMatchClass()
    {
        var state = new CheckConfigurationState();
        var cut = Render<CheckConfigurationResults>(p => p.Add(c => c.State, state));

        state.SetResults([new PrinterConfigurationValue("ezpl.print_width", "799", ConfigurationValueMatch.Matches)]);

        cut.WaitForAssertion(() =>
        {
            var value = cut.Find("[data-testid='check-configuration-results'] .config-list-value");
            Assert.Contains("config-value-match", value.ClassList);
            Assert.DoesNotContain("config-value-mismatch", value.ClassList);
        });
    }

    [Fact]
    public void MismatchedValue_IsRenderedWithTheMismatchClass()
    {
        var state = new CheckConfigurationState();
        var cut = Render<CheckConfigurationResults>(p => p.Add(c => c.State, state));

        state.SetResults([new PrinterConfigurationValue("ezpl.print_width", "812", ConfigurationValueMatch.Mismatch)]);

        cut.WaitForAssertion(() =>
        {
            var value = cut.Find("[data-testid='check-configuration-results'] .config-list-value");
            Assert.Contains("config-value-mismatch", value.ClassList);
            Assert.DoesNotContain("config-value-match", value.ClassList);
        });
    }

    [Fact]
    public void InformationalValue_HasNeitherColourClass()
    {
        var state = new CheckConfigurationState();
        var cut = Render<CheckConfigurationResults>(p => p.Add(c => c.State, state));

        state.SetResults([new PrinterConfigurationValue("wlan.security", "wpa psk", ConfigurationValueMatch.Informational)]);

        cut.WaitForAssertion(() =>
        {
            var value = cut.Find("[data-testid='check-configuration-results'] .config-list-value");
            Assert.DoesNotContain("config-value-match", value.ClassList);
            Assert.DoesNotContain("config-value-mismatch", value.ClassList);
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
