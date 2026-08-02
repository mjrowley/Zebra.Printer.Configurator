using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Logging;
using Zebra.Printer.Configurator.UI.Layout;

namespace Zebra.Printer.Configurator.ComponentTests.Layout;

public class MainLayoutTests : BunitContext
{
    private readonly AppLog _appLog = new();

    public MainLayoutTests()
    {
        Services.AddSingleton<IAppLog>(_appLog);
    }

    [Fact]
    public void InitialRender_WithNoEntries_ShowsEmptyState()
    {
        var cut = Render<MainLayout>();

        Assert.Contains("No activity yet.", cut.Markup);
    }

    [Fact]
    public void WhenEntryLogged_ShowsItInThePanel()
    {
        var cut = Render<MainLayout>();

        _appLog.Log("Waiting for NFC tap...");

        cut.WaitForAssertion(() => Assert.Contains("Waiting for NFC tap...", cut.Find("[data-testid='log-panel']").TextContent));
    }

    [Fact]
    public void MultipleEntries_AreShownNewestFirst()
    {
        var cut = Render<MainLayout>();

        _appLog.Log("First event");
        _appLog.Log("Second event");

        cut.WaitForAssertion(() =>
        {
            var entries = cut.FindAll("[data-testid='log-entry']");
            Assert.Equal(2, entries.Count);
            Assert.Contains("Second event", entries[0].TextContent);
            Assert.Contains("First event", entries[1].TextContent);
        });
    }
}
