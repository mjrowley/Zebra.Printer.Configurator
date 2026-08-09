using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Firmware;
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
    private readonly IPrinterVersionCheckService _versionCheckService = Substitute.For<IPrinterVersionCheckService>();
    private readonly IFirmwareUpdateLauncher _firmwareUpdateLauncher = Substitute.For<IFirmwareUpdateLauncher>();
    private readonly FirmwareUpdateStatusMonitor _updateStatusMonitor = new();

    private async Task<(PairAndConfigureWorkflow Workflow, PairingSession Session)> RunWorkflowToCompletionAsync(
        ConnectionTestResult connectivityResult, WlanConfiguration? configuration = null)
    {
        configuration ??= Configuration;
        var sessionFactory = Substitute.For<IPrinterConnectionSessionFactory>();
        sessionFactory.OpenAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IPrinterConnectionSession>()));
        var configurationService = Substitute.For<IPrinterConfigurationService>();
        var pdfDirectService = Substitute.For<IPdfDirectService>();
        var restartService = Substitute.For<IPrinterRestartService>();
        var connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
        connectivityTestService.TestConnectionAsync(Device, configuration, Arg.Any<CancellationToken>()).Returns(connectivityResult);

        var workflow = new PairAndConfigureWorkflow(sessionFactory, configurationService, pdfDirectService, restartService, connectivityTestService);
        await workflow.RunAsync(Device, configuration);

        var session = new PairingSession { Device = Device, Configuration = configuration };
        Services.AddSingleton(workflow);
        Services.AddSingleton(session);
        Services.AddSingleton(_factoryResetService);
        Services.AddSingleton(_pairingService);
        Services.AddSingleton(_configurationReader);
        Services.AddSingleton(_connectivityMonitor);
        Services.AddSingleton(_wifiMonitor);
        Services.AddSingleton<IPrinterConnectionModeProvider>(_connectionModeProvider);
        Services.AddSingleton(_versionCheckService);
        Services.AddSingleton(_firmwareUpdateLauncher);
        Services.AddSingleton(_updateStatusMonitor);

        // Defaults to "up to date" (renders nothing) - most tests here don't care about the
        // firmware/version check at all, so this keeps them unaffected unless a specific test
        // overrides it.
        _versionCheckService.CheckAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate });

        return (workflow, session);
    }

    private static TcpListener StartLoopbackListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
    public async Task SucceededWorkflow_WhenFirmwareNeedsUpdate_ShowsAlertWithUpdateFirmwareEnabled()
    {
        // Confirms the deadlock-fix path: Pairing.razor may have let NeedsUpdate through unblocked
        // if WiFi wasn't available at that point - Result.razor re-runs the same check here, where
        // the printer's WiFi has just been confirmed working by the workflow itself, so the update
        // is actually offered for real this time.
        _connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        // Configured after RunWorkflowToCompletionAsync, whose own setup would otherwise overwrite
        // this with the default "up to date" response for the same (device, token) call signature.
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" });

        var cut = Render<Result>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-needs-update']")));
        Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ClickingUpdateFirmware_PassesTheRealConfirmedStaticIp_NotALiteralFieldName()
    {
        // Regression test for a real bug: PrinterVersionAlert's WifiIpAddress bound without an @
        // prefix (WifiIpAddress="Session.Configuration?.StaticIpAddress") passed that literal text
        // instead of its value, since a string literal is itself valid for a string-typed component
        // parameter - it compiled fine but sent garbage to the SDK's TcpConnection. Only rendering
        // the full page (not the isolated component via the test-harness parameter API) exercises
        // the actual Razor markup that had the bug.
        _connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        var bundle = new FirmwareBundle
        {
            ModelName = "ZD421",
            ExpectedLinkOsVersion = new LinkOsVersion(7, 6, 2),
            ExpectedFirmwareVersion = "V93.21.49Z",
            FirmwareAssetLogicalPath = "ZD421_Firmware/V93.21.49Z.zpl",
        };
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" });
        _firmwareUpdateLauncher.StartAsync(Device, bundle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var cut = Render<Result>();
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='update-firmware-button']").Click();

        cut.WaitForAssertion(() =>
            _ = _firmwareUpdateLauncher.Received(1).StartAsync(Device, bundle, "192.168.1.50", Arg.Any<CancellationToken>()));
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
        // A closed loopback port refuses the probe almost instantly, unlike a real unreachable IP
        // which can take the full probe timeout to fail - keeps this test fast and deterministic.
        var configuration = Configuration with { StaticIpAddress = "127.0.0.1" };
        var (_, session) = await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"), configuration);
        var cut = Render<Result>(p => p.Add(c => c.WifiProbePort, GetFreeLoopbackPort()));

        cut.Find("[data-testid='reconfigure-button']").Click();

        Assert.Same(Device, session.Device);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/configure", navigation.Uri), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WhileFactoryResetIsSelected_ReconfigurePrinterButtonIsDisabled()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        var cut = Render<Result>();

        cut.Find("[data-testid='factory-reset-button']").Click();

        Assert.True(cut.Find("[data-testid='reconfigure-button']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ClickingReconfigure_WhenStaticIpIsReachable_SwitchesToWifiAndStartsWifiMonitor()
    {
        using var listener = StartLoopbackListener(out var port);
        var configuration = Configuration with { StaticIpAddress = "127.0.0.1" };
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"), configuration);
        var cut = Render<Result>(p => p.Add(c => c.WifiProbePort, port));

        cut.Find("[data-testid='reconfigure-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(PrinterConnectionMode.Wifi, _connectionModeProvider.Mode);
            Assert.Equal("127.0.0.1", _connectionModeProvider.WifiIpAddress);
            Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
            Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Wifi);
        });
        _wifiMonitor.Received().Start("127.0.0.1");
    }

    [Fact]
    public async Task ClickingReconfigure_WhenStaticIpIsUnreachable_FallsBackToBluetooth()
    {
        var configuration = Configuration with { StaticIpAddress = "127.0.0.1" };
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"), configuration);
        var cut = Render<Result>(p => p.Add(c => c.WifiProbePort, GetFreeLoopbackPort()));

        cut.Find("[data-testid='reconfigure-button']").Click();

        cut.WaitForAssertion(() => Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode), TimeSpan.FromSeconds(5));
        _wifiMonitor.DidNotReceive().Start(Arg.Any<string>());
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
        var sessionFactory = Substitute.For<IPrinterConnectionSessionFactory>();
        var configurationService = Substitute.For<IPrinterConfigurationService>();
        var pdfDirectService = Substitute.For<IPdfDirectService>();
        var restartService = Substitute.For<IPrinterRestartService>();
        var connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
        var workflow = new PairAndConfigureWorkflow(sessionFactory, configurationService, pdfDirectService, restartService, connectivityTestService);
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
}
