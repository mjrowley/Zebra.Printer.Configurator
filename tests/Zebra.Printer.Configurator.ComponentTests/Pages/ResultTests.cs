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
        PrinterName = "ZD421",
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
    private readonly IBagTagTemplateService _templateService = Substitute.For<IBagTagTemplateService>();
    private readonly PrinterActivityMonitor _activityMonitor = new();
    private readonly IWebInterfaceService _webInterfaceService = Substitute.For<IWebInterfaceService>();
    private readonly IPrinterStatusReader _statusReader = Substitute.For<IPrinterStatusReader>();

    private static PrinterStatus DefaultPrinterStatus() => new()
    {
        VersionResult = new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate },
        WebInterfaceState = new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true },
        ConfigurationValues = Array.Empty<PrinterConfigurationValue>(),
    };

    // Overrides the merged IPrinterStatusReader read - the single Bluetooth read that now drives
    // PrinterVersionAlert/WebInterfaceTogglePanel/CheckConfigurationResults' initial content
    // together (see IPrinterStatusReader's own doc comment). Call after RunWorkflowToCompletionAsync,
    // whose own setup would otherwise overwrite this with the default "up to date, web interface
    // already on, no configuration values" response for the same (Device, token) call signature.
    private void StubStatus(
        PrinterVersionOutcome outcome = PrinterVersionOutcome.UpToDate,
        string? linkOsVersionFound = null,
        string? firmwareVersionFound = null,
        FirmwareBundle? bundle = null,
        bool webInterfaceEnabled = true,
        IReadOnlyList<PrinterConfigurationValue>? configurationValues = null)
    {
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new PrinterStatus
            {
                VersionResult = new PrinterVersionCheckResult
                {
                    Outcome = outcome,
                    Bundle = bundle,
                    LinkOsVersionFound = linkOsVersionFound,
                    FirmwareVersionFound = firmwareVersionFound,
                },
                WebInterfaceState = new WebInterfaceState { HttpsEnabled = webInterfaceEnabled, HttpEnabled = webInterfaceEnabled },
                ConfigurationValues = configurationValues ?? Array.Empty<PrinterConfigurationValue>(),
            });
    }

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
        Services.AddSingleton(_templateService);
        Services.AddSingleton(_activityMonitor);
        Services.AddSingleton(_webInterfaceService);
        Services.AddSingleton(_statusReader);

        // _webInterfaceService/_versionCheckService are still legitimately used directly by
        // WebInterfaceTogglePanel's Retry()/CloseComplete() self-heal reads and
        // PrinterVersionAlert's post-firmware-update-success recheck respectively - kept here
        // (harmlessly unused by most tests) for the same reason as before.
        _versionCheckService.CheckAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate });
        _webInterfaceService.ReadStateAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });

        // Defaults to "nothing already on the printer" - most tests here don't care about the bag
        // tag templates panel at all, so this keeps them unaffected unless a specific test overrides it.
        _templateService.GetExistingTemplateFileNamesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        // Defaults to "up to date, web interface already on, no configuration values" - matches the
        // pre-merge defaults above so most tests here (which don't care about the merged status read
        // at all) stay unaffected unless a specific test overrides this via StubStatus.
        _statusReader.ReadStatusAsync(Arg.Any<PrinterDevice>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(DefaultPrinterStatus());

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
        StubStatus(outcome: PrinterVersionOutcome.NeedsUpdate, linkOsVersionFound: "7.5.0", firmwareVersionFound: "V93.21.06Z");

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
        StubStatus(outcome: PrinterVersionOutcome.NeedsUpdate, bundle: bundle, linkOsVersionFound: "7.5.0", firmwareVersionFound: "V93.21.06Z");
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

    [Theory]
    [InlineData(true, "enabled", "text-success")]
    [InlineData(false, "disabled", "text-danger")]
    public async Task SucceededWorkflow_ShowsWebInterfaceStatusLine_ColoredByEnabledState(bool enabled, string expectedWord, string expectedClass)
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        StubStatus(webInterfaceEnabled: enabled);

        var cut = Render<Result>();

        cut.WaitForAssertion(() =>
        {
            var statusLine = cut.Find("[data-testid='web-interface-status']");
            Assert.Contains($"currently {expectedWord}", statusLine.TextContent);
            Assert.Contains(expectedClass, statusLine.ClassList);
        });
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
    public async Task WhileWebInterfaceToggleIsApplying_ReconfigureAndCheckConfigurationAreDisabled()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        var setEnabledTcs = new TaskCompletionSource();
        _webInterfaceService.SetEnabledAsync(Device, false, Arg.Any<CancellationToken>()).Returns(setEnabledTcs.Task);
        var cut = Render<Result>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='reconfigure-button']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
        });
        setEnabledTcs.SetResult();
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
        // Result.razor injects IPrinterStatusReader/PrinterActivityMonitor at the page level (unlike
        // the other per-purpose services below, which are only injected by children that mount
        // inside the Succeeded branch) - they must be registered even though this
        // redirect-before-render path never actually uses them.
        Services.AddSingleton(_statusReader);
        Services.AddSingleton(_activityMonitor);

        Render<Result>();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
    }

    [Fact]
    public async Task SucceededWorkflow_AutomaticallyFetchesPrinterStatusOnce()
    {
        // The merged read now runs automatically as soon as the page shows the Succeeded state (via
        // OnInitializedAsync), rather than only after a manual "Check Configuration" click.
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));
        StubStatus(configurationValues: [new PrinterConfigurationValue("device.friendly_name", "Warehouse-01")]);

        var cut = Render<Result>();

        cut.WaitForAssertion(() => Assert.Contains("Warehouse-01", cut.Find("[data-testid='check-configuration-results']").TextContent));
        _ = _statusReader.Received(1).ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>());
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
