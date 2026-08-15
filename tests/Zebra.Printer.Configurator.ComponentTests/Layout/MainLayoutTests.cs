using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Logging;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Layout;

namespace Zebra.Printer.Configurator.ComponentTests.Layout;

public class MainLayoutTests : BunitContext
{
    private readonly AppLog _appLog = new();
    private readonly FakeAppVersionProvider _appVersionProvider = new();
    private readonly PrinterConnectivityMonitor _connectivityMonitor = new();
    private readonly PairingSession _session = new();
    private readonly FirmwareUpdateStatusMonitor _updateStatusMonitor = new();
    private readonly PrinterActivityMonitor _activityMonitor = new();

    public MainLayoutTests()
    {
        Services.AddSingleton<IAppLog>(_appLog);
        Services.AddSingleton<IAppVersionProvider>(_appVersionProvider);
        Services.AddSingleton(_connectivityMonitor);

        // MainLayout renders CancelWorkflowButton and BackToPairingButton, which need all of these -
        // built from substitutes the same way PairAndConfigureWorkflowTests.CreateWorkflow() does,
        // since nothing in these tests drives the workflow itself.
        Services.AddSingleton(new PairAndConfigureWorkflow(
            Substitute.For<IPrinterConnectionSessionFactory>(),
            Substitute.For<IPrinterConfigurationService>(),
            Substitute.For<IPdfDirectService>(),
            Substitute.For<IPrinterRestartService>(),
            Substitute.For<IPrinterConnectivityTestService>()));
        Services.AddSingleton(new PrinterOperationCancellation());
        Services.AddSingleton(_session);
        Services.AddSingleton(_updateStatusMonitor);
        Services.AddSingleton(_activityMonitor);
        Services.AddSingleton(Substitute.For<IWifiConnectivityMonitor>());
        Services.AddSingleton<IPrinterConnectionModeProvider>(new PrinterConnectionModeProvider());
    }

    [Fact]
    public void InitialRender_WithNoEntries_ShowsEmptyState()
    {
        var cut = Render<MainLayout>();

        Assert.Contains("No activity yet.", cut.Markup);
    }

    [Fact]
    public void InitialRender_ShowsAppName()
    {
        var cut = Render<MainLayout>();

        Assert.Contains("Zebra Printer Configurator", cut.Find("[data-testid='app-header']").TextContent);
    }

    [Fact]
    public void InitialRender_ShowsAppVersion()
    {
        var cut = Render<MainLayout>();

        Assert.Equal("v1.2 (3)", cut.Find("[data-testid='app-version']").TextContent);
    }

    [Fact]
    public void InitialRender_ShowsBothIndicatorsDisconnected()
    {
        var cut = Render<MainLayout>();

        Assert.Contains("state-disconnected", cut.Find("[data-testid='bluetooth-indicator']").ClassList);
        Assert.Contains("state-disconnected", cut.Find("[data-testid='wifi-indicator']").ClassList);
    }

    [Fact]
    public void WhenConnectivityMonitorChanges_IndicatorsUpdate()
    {
        var cut = Render<MainLayout>();

        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);
        _connectivityMonitor.SetWifi(ConnectionIndicatorState.Error);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("state-connected", cut.Find("[data-testid='bluetooth-indicator']").ClassList);
            Assert.Contains("state-error", cut.Find("[data-testid='wifi-indicator']").ClassList);
        });
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

    private sealed class FakeAppVersionProvider : IAppVersionProvider
    {
        public string VersionLabel => "v1.2 (3)";
    }
}
