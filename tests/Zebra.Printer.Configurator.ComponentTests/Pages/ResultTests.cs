using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Pages;

namespace Zebra.Printer.Configurator.ComponentTests.Pages;

public class ResultTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private static readonly WlanConfiguration Configuration = new()
    {
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    private readonly IPrinterFactoryResetService _factoryResetService = Substitute.For<IPrinterFactoryResetService>();
    private readonly IBluetoothPairingService _pairingService = Substitute.For<IBluetoothPairingService>();
    private readonly IPrinterConfigurationReader _configurationReader = Substitute.For<IPrinterConfigurationReader>();
    private readonly PrinterConnectivityMonitor _connectivityMonitor = new();
    private readonly IWifiConnectivityMonitor _wifiMonitor = Substitute.For<IWifiConnectivityMonitor>();
    private readonly PrinterConnectionModeProvider _connectionModeProvider = new();

    private async Task<(PairAndConfigureWorkflow Workflow, PairingSession Session)> RunWorkflowToCompletionAsync(ConnectionTestResult connectivityResult)
    {
        var configurationService = Substitute.For<IPrinterConfigurationService>();
        var restartService = Substitute.For<IPrinterRestartService>();
        var connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
        connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>()).Returns(connectivityResult);

        var workflow = new PairAndConfigureWorkflow(configurationService, restartService, connectivityTestService);
        await workflow.RunAsync(Device, Configuration);

        var session = new PairingSession { Device = Device, Configuration = Configuration };
        Services.AddSingleton(workflow);
        Services.AddSingleton(session);
        Services.AddSingleton(_factoryResetService);
        Services.AddSingleton(_pairingService);
        Services.AddSingleton(_configurationReader);
        Services.AddSingleton(_connectivityMonitor);
        Services.AddSingleton(_wifiMonitor);
        Services.AddSingleton<IPrinterConnectionModeProvider>(_connectionModeProvider);

        return (workflow, session);
    }

    [Fact]
    public async Task SucceededWorkflow_ShowsConfirmedSsidAndIp()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));

        var cut = Render<Result>();

        var successElement = cut.Find("[data-testid='result-success']");
        Assert.Contains("Warehouse-WiFi", successElement.TextContent);
        Assert.Contains("192.168.1.50", successElement.TextContent);
    }

    [Fact]
    public async Task FailedWorkflow_ShowsFailureReasonAndRetryButton()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Failed("Printer did not respond."));

        var cut = Render<Result>();

        var failureElement = cut.Find("[data-testid='result-failure']");
        Assert.Contains("Printer did not respond.", failureElement.TextContent);
        Assert.NotNull(cut.Find("button"));
    }

    [Fact]
    public async Task SucceededWorkflow_ShowsReconfigureFactoryResetAndCheckConfigurationButtons()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));

        var cut = Render<Result>();

        Assert.Contains("Reconfigure Printer", cut.Markup);
        Assert.NotNull(cut.Find("[data-testid='factory-reset-button']"));
        Assert.NotNull(cut.Find("[data-testid='check-configuration-button']"));
    }

    [Fact]
    public async Task ClickingReconfigure_NavigatesToConfigureWithoutResettingSession()
    {
        var (_, session) = await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        var cut = Render<Result>();

        cut.Find("button").Click(); // "Reconfigure Printer" - first button in the succeeded branch

        Assert.Same(Device, session.Device);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/configure", navigation.Uri);
    }

    [Fact]
    public async Task WhileFactoryResetIsSelected_ReconfigurePrinterButtonIsDisabled()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        var cut = Render<Result>();

        cut.Find("[data-testid='factory-reset-button']").Click();

        Assert.True(cut.Find("button").HasAttribute("disabled")); // "Reconfigure Printer" - first button
    }

    [Fact]
    public async Task WhileFactoryResetIsSelected_CheckConfigurationButtonIsDisabled()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        var cut = Render<Result>();

        cut.Find("[data-testid='factory-reset-button']").Click();

        Assert.True(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ClickingRetry_ResetsSessionAndNavigatesToPairing()
    {
        var (_, session) = await RunWorkflowToCompletionAsync(ConnectionTestResult.Failed("Printer did not respond."));
        var cut = Render<Result>();

        cut.Find("button").Click();

        Assert.Null(session.Device);
        Assert.Null(session.Configuration);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
    }

    [Fact]
    public void WhenWorkflowHasNotFinished_RedirectsToPairing()
    {
        var configurationService = Substitute.For<IPrinterConfigurationService>();
        var restartService = Substitute.For<IPrinterRestartService>();
        var connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
        var workflow = new PairAndConfigureWorkflow(configurationService, restartService, connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession());
        Services.AddSingleton(_connectivityMonitor);
        Services.AddSingleton(_wifiMonitor);
        Services.AddSingleton<IPrinterConnectionModeProvider>(_connectionModeProvider);

        Render<Result>();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
    }

    [Fact]
    public async Task ClickingRetry_ResetsConnectivityMonitorConnectionModeAndStopsWifiMonitor()
    {
        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);
        _connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        _connectionModeProvider.UseWifi("192.168.1.50");
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Failed("Printer did not respond."));
        var cut = Render<Result>();

        cut.Find("button").Click();

        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Wifi);
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
        _wifiMonitor.Received().Stop();
    }

    [Fact]
    public async Task SucceededWorkflow_WhenBluetoothConnected_ConnectViaWifiButtonIsEnabled()
    {
        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));

        var cut = Render<Result>();

        Assert.False(cut.Find("[data-testid='connect-via-wifi-button']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task SucceededWorkflow_WhenBluetoothNotConnected_ConnectViaWifiButtonIsDisabled()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));

        var cut = Render<Result>();

        Assert.True(cut.Find("[data-testid='connect-via-wifi-button']").HasAttribute("disabled"));
    }
}
