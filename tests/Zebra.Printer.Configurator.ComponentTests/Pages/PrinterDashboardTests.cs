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

// Covers PrinterDashboard.razor - the "printer is paired/configured" home screen reached either
// straight from Pairing.razor (a fresh pair or a restored session - _justConfigured false, "Printer
// Paired" heading, RenderArrivedFromPairing below) or from Result.razor after a successful
// Configure -> Progress run (_justConfigured true, "Success" heading + confirmed Ssid/IP,
// RunWorkflowToCompletionAsync + RenderDashboard below). Most action-menu/firmware/web-interface
// behavior is identical either way and only tested once (via the simpler Pairing-arrival setup) - the
// handful of tests that specifically depend on _justConfigured (the success banner, the Reconfigure
// network-probe path) use the Configure-arrival setup instead.
public class PrinterDashboardTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF", SerialNumber = "12345" };

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

    private readonly IBluetoothPairingService _pairingService = Substitute.For<IBluetoothPairingService>();
    private readonly FakePrinterFactoryResetService _factoryResetService = new();
    private readonly IPrinterConfigurationReader _configurationReader = Substitute.For<IPrinterConfigurationReader>();
    private readonly IAppLog _appLog = Substitute.For<IAppLog>();
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
    private readonly IPrinterCalibrationService _calibrationService = Substitute.For<IPrinterCalibrationService>();

    public PrinterDashboardTests()
    {
        // Registered once here (not per render-helper) so a test's own StubStatus()/
        // ReadConfigurationAsync override - configured in the test body, which always runs after this
        // constructor - reliably wins over these generic defaults, matching NSubstitute's
        // last-configured-match-wins behavior. Covers both arrival paths below.
        Services.AddSingleton<IBluetoothPairingService>(_pairingService);
        Services.AddSingleton<IPrinterFactoryResetService>(_factoryResetService);
        Services.AddSingleton(_configurationReader);
        Services.AddSingleton(_appLog);
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
        Services.AddSingleton(_calibrationService);

        _webInterfaceService.ReadStateAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
        _versionCheckService.CheckAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate });
        _templateService.GetExistingTemplateFileNamesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        _configurationReader.ReadConfigurationAsync(Arg.Any<PrinterDevice>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PrinterConfigurationValue>());
        _statusReader.ReadStatusAsync(Arg.Any<PrinterDevice>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(DefaultPrinterStatus());
    }

    private static PrinterStatus DefaultPrinterStatus() => new()
    {
        VersionResult = new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate },
        WebInterfaceState = new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true },
        ConfigurationValues = Array.Empty<PrinterConfigurationValue>(),
    };

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

    private IRenderedComponent<PrinterDashboard> RenderDashboard(int? wifiProbePort = null) =>
        wifiProbePort is { } port
            ? Render<PrinterDashboard>(p => p.Add(c => c.WifiProbePort, port))
            : Render<PrinterDashboard>();

    // Arrives the way Pairing.razor hands off - Session.Device already set, no workflow run this
    // session (Workflow.State stays NotStarted, so PrinterDashboard's _justConfigured is false) - the
    // "Printer Paired" heading, CheckWifiConnectivityAsync's own Bluetooth WLAN-list read for the IP.
    private IRenderedComponent<PrinterDashboard> RenderArrivedFromPairing(int? wifiProbePort = null)
    {
        var workflow = new PairAndConfigureWorkflow(
            Substitute.For<IPrinterConnectionSessionFactory>(),
            Substitute.For<IPrinterConfigurationService>(),
            Substitute.For<IPdfDirectService>(),
            Substitute.For<IPrinterRestartService>(),
            Substitute.For<IPrinterConnectivityTestService>());
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession { Device = Device });

        var cut = RenderDashboard(wifiProbePort);
        // Renders (and everything gated on it, including this testid) only once the automatic WiFi
        // check fully resolves, which can take up to its own probe timeout for an
        // unreachable-port scenario - longer than bUnit's default wait window covers.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='discovered-device']")), TimeSpan.FromSeconds(5));
        return cut;
    }

    // Arrives the way Result.razor hands off - a real PairAndConfigureWorkflow.RunAsync just
    // completed Succeeded, so WiFi is already confirmed (Workflow.Result.ResolvedIpAddress) and
    // _justConfigured is true. Registration only (no render) so a test can still override StubStatus/
    // ReadConfigurationAsync in between this and RenderDashboard, same as the sync arrival path above.
    private async Task RunWorkflowToCompletionAsync(ConnectionTestResult connectivityResult, WlanConfiguration? configuration = null)
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

        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession { Device = Device, Configuration = configuration });
    }

    private static void OpenActionsMenuAndClickItem(IRenderedComponent<PrinterDashboard> cut, string menuItemTestId)
    {
        cut.Find("[data-testid='printer-actions-menu-button']").Click();
        cut.Find($"[data-testid='{menuItemTestId}']").Click();
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

    // ---- Pairing-arrival tests ----

    [Fact]
    public void ArrivedFromPairing_ShowsPrinterPairedHeadingAndDevice()
    {
        var cut = RenderArrivedFromPairing();

        Assert.Contains("Printer Paired", cut.Find("h1").TextContent);
        Assert.Contains("12345", cut.Find("[data-testid='discovered-device']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='configure-printer-button']"));
    }

    [Fact]
    public void ArrivedFromPairing_ClickingConfigure_NavigatesToConfigureWithoutResettingSession()
    {
        var cut = RenderArrivedFromPairing();

        cut.Find("[data-testid='configure-printer-button']").Click();

        var session = Services.GetRequiredService<PairingSession>();
        Assert.Same(Device, session.Device);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/configure", navigation.Uri);
    }

    [Fact]
    public void ClickingAboutPrinter_NavigatesToInfo()
    {
        var cut = RenderArrivedFromPairing();

        cut.Find("[data-testid='dashboard-info-link']").Click();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/dashboard/info", navigation.Uri);
    }

    [Fact]
    public void ClickingFactoryReset_ShowsConfirmationWarning()
    {
        var cut = RenderArrivedFromPairing();

        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        Assert.NotNull(cut.Find("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public void CancellingFactoryResetConfirmation_ReturnsToDashboardWithoutCallingService()
    {
        var cut = RenderArrivedFromPairing();
        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.NotNull(cut.Find("[data-testid='discovered-device']"));
        Assert.Empty(cut.FindAll("[data-testid='factory-reset-warning']"));
        Assert.Null(_factoryResetService.LastResetMacAddress);
    }

    [Fact]
    public void ShowsRecheckConfigurationMenuItem()
    {
        var cut = RenderArrivedFromPairing();

        Assert.NotNull(cut.Find("[data-testid='menu-item-recheck-configuration']"));
    }

    [Theory]
    [InlineData(true, "enabled", "status-text-success")]
    [InlineData(false, "disabled", "status-text-error")]
    public void ShowsWebInterfaceStatusLine_ColoredByEnabledState(bool enabled, string expectedWord, string expectedClass)
    {
        StubStatus(webInterfaceEnabled: enabled);

        var cut = RenderArrivedFromPairing();

        cut.WaitForAssertion(() =>
        {
            var statusLine = cut.Find("[data-testid='web-interface-status']");
            Assert.Contains($"currently {expectedWord}", statusLine.TextContent);
            Assert.Contains(expectedClass, statusLine.ClassList);
        });
    }

    [Fact]
    public void WebInterfaceTogglePanelOwnRetry_AfterMergedReadFailure_UpdatesTopStatusLine()
    {
        // The top "Web Interface is currently..." line must reflect whatever WebInterfaceTogglePanel
        // itself most recently confirmed via its own reads (Retry()/CloseComplete()), not just stay
        // frozen at the last merged/Recheck read - this covers the case where the merged read failed
        // entirely (so the panel starts in its own Failed/Try-Again state) and the panel's own retry
        // succeeds.
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PrinterStatus>(new InvalidOperationException("simulated status check failure")));
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });

        var cut = RenderArrivedFromPairingWithoutWaitingForStatus();
        cut.WaitForAssertion(() => Assert.Contains("Could not check web interface status.", cut.Find("[data-testid='web-interface-error']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='web-interface-status']"));

        cut.FindAll("md-filled-button").First(b => b.TextContent.Trim() == "Try Again").Click();

        cut.WaitForAssertion(() =>
        {
            var statusLine = cut.Find("[data-testid='web-interface-status']");
            Assert.Contains("currently enabled", statusLine.TextContent);
            Assert.Contains("status-text-success", statusLine.ClassList);
        });
    }

    // A failed merged status read never renders [data-testid='discovered-device'] via the version
    // alert's own gating the way the happy path does, but the device summary line itself is
    // unconditional - waiting on that instead of RenderArrivedFromPairing's own wait target.
    private IRenderedComponent<PrinterDashboard> RenderArrivedFromPairingWithoutWaitingForStatus()
    {
        var workflow = new PairAndConfigureWorkflow(
            Substitute.For<IPrinterConnectionSessionFactory>(),
            Substitute.For<IPrinterConfigurationService>(),
            Substitute.For<IPdfDirectService>(),
            Substitute.For<IPrinterRestartService>(),
            Substitute.For<IPrinterConnectivityTestService>());
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession { Device = Device });
        return RenderDashboard();
    }

    [Fact]
    public void AutomaticallyPopulatesConfigurationListWithoutClicking()
    {
        // The single merged Bluetooth read now covers the configuration list too, so it appears as
        // soon as the dashboard loads - no click needed (unlike the old per-purpose reads this
        // replaced).
        StubStatus(configurationValues: [new PrinterConfigurationValue("device.friendly_name", "Warehouse-01")]);

        var cut = RenderArrivedFromPairing();

        cut.WaitForAssertion(() => Assert.Contains("Warehouse-01", cut.Find("[data-testid='check-configuration-results']").TextContent));
    }

    [Fact]
    public void ClickingRecheckConfiguration_RefreshesFirmwareStatus_WebInterfaceState_AndConfigurationList()
    {
        using var listener = StartLoopbackListener(out var port);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        var firstStatus = new PrinterStatus
        {
            VersionResult = new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate },
            WebInterfaceState = new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false },
            ConfigurationValues = [new PrinterConfigurationValue("device.friendly_name", "Before-Recheck")],
        };
        var secondStatus = new PrinterStatus
        {
            VersionResult = new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" },
            WebInterfaceState = new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true },
            ConfigurationValues = [new PrinterConfigurationValue("device.friendly_name", "After-Recheck")],
        };
        // The recheck read is stubbed as a genuinely pending Task (resolved explicitly below) rather
        // than an instantly-completed one - a real Bluetooth read always has an observable async gap,
        // and PrinterVersionAlert/WebInterfaceTogglePanel only pick up a merged update by observing
        // StatusLoading go true->false across two separate renders. An instantly-resolving mock lets
        // Blazor coalesce both renders into one, which would never happen against real hardware.
        var rechecktcs = new TaskCompletionSource<PrinterStatus>();
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(firstStatus), rechecktcs.Task);
        var cut = RenderArrivedFromPairing(wifiProbePort: port);
        cut.WaitForAssertion(() => Assert.Equal("Enable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim()));
        cut.WaitForAssertion(() => Assert.Contains("Before-Recheck", cut.Find("[data-testid='check-configuration-results']").TextContent));

        OpenActionsMenuAndClickItem(cut, "menu-item-recheck-configuration");

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled")));

        rechecktcs.SetResult(secondStatus);

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='version-check-needs-update']"));
            Assert.Equal("Disable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim());
            Assert.Contains("After-Recheck", cut.Find("[data-testid='check-configuration-results']").TextContent);
        });
    }

    [Fact]
    public void WhileFactoryResetIsSelected_ConfigureButtonAndRecheckAreDisabled()
    {
        var cut = RenderArrivedFromPairing();

        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled"));
    }

    [Fact]
    public void WhileWebInterfaceToggleIsApplying_ConfigureAndRecheckAreDisabled()
    {
        var setEnabledTcs = new TaskCompletionSource();
        _webInterfaceService.SetEnabledAsync(Device, false, Arg.Any<CancellationToken>()).Returns(setEnabledTcs.Task);
        var cut = RenderArrivedFromPairing();

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled"));
        });
        setEnabledTcs.SetResult();
    }

    [Fact]
    public void ShowsCalibrateMediaMenuItem()
    {
        var cut = RenderArrivedFromPairing();

        Assert.NotNull(cut.Find("[data-testid='menu-item-calibrate-media']"));
    }

    [Fact]
    public void WhileCalibratingMedia_ConfigureAndRecheckAndDisconnectAreDisabled()
    {
        var calibrateTcs = new TaskCompletionSource();
        _calibrationService.CalibrateAsync(Device, Arg.Any<CancellationToken>()).Returns(calibrateTcs.Task);
        var cut = RenderArrivedFromPairing();
        OpenActionsMenuAndClickItem(cut, "menu-item-calibrate-media");

        cut.Find("[data-testid='calibrate-media-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='dashboard-disconnect-button']").HasAttribute("disabled"));
        });
        calibrateTcs.SetResult();
    }

    [Fact]
    public void WhileFactoryResetIsSelected_DisconnectButtonIsDisabled()
    {
        var cut = RenderArrivedFromPairing();

        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        Assert.True(cut.Find("[data-testid='dashboard-disconnect-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingDisconnect_NavigatesToPairingAndResetsConnectivityMonitorAndConnectionMode()
    {
        _connectionModeProvider.UseWifi("192.168.1.50");
        var cut = RenderArrivedFromPairing();

        cut.Find("[data-testid='dashboard-disconnect-button']").Click();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
        var session = Services.GetRequiredService<PairingSession>();
        Assert.Null(session.Device);
    }

    [Fact]
    public void ClickingDisconnect_WhileStatusCheckStillInFlight_LateResultDoesNotThrow()
    {
        // Regression guard for the race this button introduces: LoadPrinterStatusAsync doesn't
        // actually cancel the in-flight Bluetooth read (interrupting a blocking SDK call is out of
        // scope, same as LinkOsPrinterVersionCheckService's own documented choice) - it just needs to
        // discard a late result instead of throwing/resurrecting it into a component that's already
        // navigated away.
        var statusTcs = new TaskCompletionSource<PrinterStatus>();
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(statusTcs.Task);
        var cut = RenderArrivedFromPairingWithoutWaitingForStatus();
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='dashboard-disconnect-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='dashboard-disconnect-button']").Click();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);

        statusTcs.SetResult(DefaultPrinterStatus());

        // Nothing should have thrown as a result of the now-stale read resolving after navigation.
        Assert.EndsWith("/", navigation.Uri);
    }

    [Fact]
    public void AfterCancellingFactoryReset_ConfigureButtonIsEnabledAgain()
    {
        var cut = RenderArrivedFromPairing();
        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.False(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void ConfirmingFactoryReset_CallsServiceWithDeviceAndShowsCompletion()
    {
        var cut = RenderArrivedFromPairing();
        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-complete']")));
        Assert.Equal("AABBCCDDEEFF", _factoryResetService.LastResetMacAddress);
        _ = _pairingService.Received(1).RemoveBondAsync("AABBCCDDEEFF", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WhenFactoryResetFails_ShowsErrorAndDoesNotRemoveBond()
    {
        _factoryResetService.ShouldThrow = true;
        var cut = RenderArrivedFromPairing();
        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-error']")));
        _ = _pairingService.DidNotReceive().RemoveBondAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WhenPrinterHasNoWifiConfigured_SetsWifiIndicatorToError()
    {
        var cut = RenderArrivedFromPairing();

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Error, _connectivityMonitor.Wifi));
        _wifiMonitor.DidNotReceive().Start(Arg.Any<string>());
    }

    [Fact]
    public void WhenPrinterIsReachableOnWifi_SetsWifiIndicatorToConnectedAndStartsMonitor()
    {
        using var listener = StartLoopbackListener(out var port);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);

        var cut = RenderArrivedFromPairing(wifiProbePort: port);

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Wifi));
        _wifiMonitor.Received().Start("127.0.0.1");
    }

    [Fact]
    public void WhenPrinterIsNotReachableOnWifi_SetsWifiIndicatorToErrorButStillStartsMonitor()
    {
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);

        // A closed loopback port refuses the probe almost instantly, unlike a real unreachable IP
        // which can take the full probe timeout to fail - keeps this test fast and deterministic.
        var cut = RenderArrivedFromPairing(wifiProbePort: GetFreeLoopbackPort());

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Error, _connectivityMonitor.Wifi), TimeSpan.FromSeconds(5));
        // Still started so the indicator keeps reflecting live reachability afterward - the printer
        // may still be finishing its own WiFi association right after a reboot/re-tap.
        _wifiMonitor.Received().Start("127.0.0.1");
    }

    [Fact]
    public void WhenVersionCheckIsUpToDate_ConfigureButtonIsEnabled()
    {
        var cut = RenderArrivedFromPairing();

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void WhenVersionCheckNeedsUpdate_AndWifiIsAvailable_ConfigureButtonIsDisabled()
    {
        using var listener = StartLoopbackListener(out var port);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        StubStatus(outcome: PrinterVersionOutcome.NeedsUpdate, linkOsVersionFound: "7.5.0", firmwareVersionFound: "V93.21.06Z");

        var cut = RenderArrivedFromPairing(wifiProbePort: port);

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void ClickingUpdateFirmware_FromPairingArrival_PassesTheRealDiscoveredWifiIpAddress()
    {
        using var listener = StartLoopbackListener(out var port);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
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
        var cut = RenderArrivedFromPairing(wifiProbePort: port);
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='update-firmware-button']").Click();

        cut.WaitForAssertion(() =>
            _ = _firmwareUpdateLauncher.Received(1).StartAsync(Device, bundle, "127.0.0.1", Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void WhenVersionCheckNeedsUpdate_AndNoWifiIsConfiguredYet_ConfigureButtonStaysEnabled()
    {
        // A never-configured printer has no WiFi yet, and "Configure Printer" is exactly what gives
        // it one - blocking here would deadlock (Configure blocked pending an update that itself
        // requires WiFi Configure hasn't set up yet). The Configure-arrival path re-surfaces this
        // same check once the printer actually has WiFi, after a successful configuration.
        StubStatus(outcome: PrinterVersionOutcome.NeedsUpdate, linkOsVersionFound: "7.5.0", firmwareVersionFound: "V93.21.06Z");

        var cut = RenderArrivedFromPairing();

        Assert.False(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void WhenVersionCheckIsUnsupported_ConfigureButtonIsDisabled_UntilSkipped()
    {
        using var listener = StartLoopbackListener(out var port);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        StubStatus(outcome: PrinterVersionOutcome.Unsupported);
        var cut = RenderArrivedFromPairing(wifiProbePort: port);
        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='version-check-skip']").Click();

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void WhenVersionCheckIsUnsupported_ClickingCancel_NavigatesToPairing()
    {
        using var listener = StartLoopbackListener(out var port);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        StubStatus(outcome: PrinterVersionOutcome.Unsupported);
        var cut = RenderArrivedFromPairing(wifiProbePort: port);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-cancel']")));

        cut.Find("[data-testid='version-check-cancel']").Click();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
    }

    // ---- Successful-configure-arrival tests ----

    [Fact]
    public async Task ArrivedFromSuccessfulConfigure_ShowsConfirmedSsidAndIp()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50"));

        var cut = RenderDashboard();

        var successElement = cut.Find("[data-testid='result-success']");
        Assert.Contains("Warehouse-WiFi", successElement.TextContent);
        Assert.Contains("192.168.1.50", successElement.TextContent);
        Assert.Contains("Success", cut.Find("h1").TextContent);
    }

    [Fact]
    public async Task ArrivedFromSuccessfulConfigure_WhenFirmwareNeedsUpdate_ShowsAlertWithUpdateFirmwareEnabled()
    {
        // Confirms the deadlock-fix path: the Pairing-arrival path may have let NeedsUpdate through
        // unblocked if WiFi wasn't available at that point - this re-runs the same check here, where
        // the printer's WiFi has just been confirmed working by the workflow itself, so the update is
        // actually offered for real this time.
        _connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50"));
        StubStatus(outcome: PrinterVersionOutcome.NeedsUpdate, linkOsVersionFound: "7.5.0", firmwareVersionFound: "V93.21.06Z");

        var cut = RenderDashboard();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-needs-update']")));
        Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ClickingUpdateFirmware_FromConfigureArrival_PassesTheRealConfirmedIp()
    {
        // Regression test for a real bug: PrinterVersionAlert's WifiIpAddress bound without an @
        // prefix passed literal text instead of the field's value, since a string literal is itself
        // valid for a string-typed component parameter - it compiled fine but sent garbage to the
        // Zebra SDK's TcpConnection. Only rendering the full page (not the isolated component via the
        // test-harness parameter API) exercises the actual Razor markup that had the bug.
        _connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50"));
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
        var cut = RenderDashboard();
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='update-firmware-button']").Click();

        cut.WaitForAssertion(() =>
            _ = _firmwareUpdateLauncher.Received(1).StartAsync(Device, bundle, "192.168.1.50", Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task ArrivedFromSuccessfulConfigure_ShowsReconfigureButtonAndActionsMenuItems()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50"));

        var cut = RenderDashboard();

        Assert.Contains("Reconfigure Printer", cut.Markup);
        Assert.NotNull(cut.Find("[data-testid='menu-item-factory-reset']"));
        Assert.NotNull(cut.Find("[data-testid='menu-item-recheck-configuration']"));
        Assert.NotNull(cut.Find("[data-testid='menu-item-calibrate-media']"));
    }

    [Theory]
    [InlineData(true, "enabled", "status-text-success")]
    [InlineData(false, "disabled", "status-text-error")]
    public async Task ArrivedFromSuccessfulConfigure_ShowsWebInterfaceStatusLine_ColoredByEnabledState(bool enabled, string expectedWord, string expectedClass)
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50"));
        StubStatus(webInterfaceEnabled: enabled);

        var cut = RenderDashboard();

        cut.WaitForAssertion(() =>
        {
            var statusLine = cut.Find("[data-testid='web-interface-status']");
            Assert.Contains($"currently {expectedWord}", statusLine.TextContent);
            Assert.Contains(expectedClass, statusLine.ClassList);
        });
    }

    [Fact]
    public async Task ClickingReconfigure_WhenResolvedIpIsReachable_SwitchesToWifiAndStartsWifiMonitor()
    {
        using var listener = StartLoopbackListener(out var port);
        var configuration = Configuration with { StaticIpAddress = "127.0.0.1" };
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "127.0.0.1"), configuration);
        var cut = RenderDashboard(wifiProbePort: port);

        cut.Find("[data-testid='configure-printer-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(PrinterConnectionMode.Wifi, _connectionModeProvider.Mode);
            Assert.Equal("127.0.0.1", _connectionModeProvider.WifiIpAddress);
            Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
            Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Wifi);
        });
        _wifiMonitor.Received().Start("127.0.0.1");
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/configure", navigation.Uri), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ClickingReconfigure_WhenResolvedIpIsUnreachable_FallsBackToBluetooth()
    {
        var configuration = Configuration with { StaticIpAddress = "127.0.0.1" };
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "127.0.0.1"), configuration);
        var cut = RenderDashboard(wifiProbePort: GetFreeLoopbackPort());

        cut.Find("[data-testid='configure-printer-button']").Click();

        cut.WaitForAssertion(() => Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode), TimeSpan.FromSeconds(5));
        _wifiMonitor.DidNotReceive().Start(Arg.Any<string>());
    }

    [Fact]
    public async Task WhileFactoryResetIsSelected_FromConfigureArrival_ConfigureButtonIsDisabled()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50"));
        var cut = RenderDashboard();

        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task WhileWebInterfaceToggleIsApplying_FromConfigureArrival_ConfigureAndRecheckAreDisabled()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50"));
        var setEnabledTcs = new TaskCompletionSource();
        _webInterfaceService.SetEnabledAsync(Device, false, Arg.Any<CancellationToken>()).Returns(setEnabledTcs.Task);
        var cut = RenderDashboard();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled"));
        });
        setEnabledTcs.SetResult();
    }

    [Fact]
    public async Task ArrivedFromSuccessfulConfigure_AutomaticallyFetchesPrinterStatusOnce()
    {
        // The merged read now runs automatically as soon as the dashboard shows (via
        // OnInitializedAsync), rather than only after a manual "Recheck Configuration" click.
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50"));
        StubStatus(configurationValues: [new PrinterConfigurationValue("device.friendly_name", "Warehouse-01")]);

        var cut = RenderDashboard();

        cut.WaitForAssertion(() => Assert.Contains("Warehouse-01", cut.Find("[data-testid='check-configuration-results']").TextContent));
        _ = _statusReader.Received(1).ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    private sealed class FakePrinterFactoryResetService : IPrinterFactoryResetService
    {
        public bool ShouldThrow { get; set; }

        public string? LastResetMacAddress { get; private set; }

        public Task ResetToFactoryDefaultsAsync(PrinterDevice device, CancellationToken cancellationToken = default)
        {
            LastResetMacAddress = device.BluetoothMacAddress;
            if (ShouldThrow)
            {
                throw new InvalidOperationException("simulated factory reset failure");
            }

            return Task.CompletedTask;
        }
    }
}
