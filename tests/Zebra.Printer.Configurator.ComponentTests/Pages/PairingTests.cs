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

public class PairingTests : BunitContext
{
    private readonly FakePrinterDiscoveryService _discoveryService = new();
    private readonly FakeBluetoothPairingService _pairingService = new();
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

    public PairingTests()
    {
        Services.AddSingleton<IPrinterDiscoveryService>(_discoveryService);
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
        Services.AddSingleton(new PairingSession());

        // _webInterfaceService/_versionCheckService are still legitimately used directly by
        // WebInterfaceTogglePanel's Retry()/CloseComplete() self-heal reads and
        // PrinterVersionAlert's post-firmware-update-success recheck respectively - neither test here
        // drives those specific paths, but the stubs are kept (harmlessly unused) for the same reason
        // _configurationReader's is: consistency with what's still real production wiring.
        _webInterfaceService.ReadStateAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
        _versionCheckService.CheckAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate });

        // Defaults to "nothing already on the printer" - most tests here don't care about the bag
        // tag templates panel at all, so this keeps them unaffected unless a specific test overrides it.
        _templateService.GetExistingTemplateFileNamesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        // Unconfigured, this NSubstitute mock resolves ReadConfigurationAsync's Task with a null
        // result by default, which the automatic post-pairing WiFi check would then throw on when
        // reading .FirstOrDefault() from it - most tests here don't care about that check at all, so
        // give it a harmless empty result unless a specific test overrides this setup itself. Still
        // used directly by Pairing.razor's own CheckWifiConnectivityAsync (unrelated to the merged
        // status read below - that's a separate WLAN-list read solely to discover the printer's
        // current IP address, not the firmware/web-interface/configuration checks).
        _configurationReader.ReadConfigurationAsync(Arg.Any<PrinterDevice>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PrinterConfigurationValue>());

        // Defaults to "up to date, web interface already on, no configuration values" - matches the
        // pre-merge defaults above so most tests here (which don't care about the merged status read
        // at all) stay unaffected unless a specific test overrides this via StubStatus.
        _statusReader.ReadStatusAsync(Arg.Any<PrinterDevice>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(DefaultPrinterStatus());
    }

    private static PrinterStatus DefaultPrinterStatus() => new()
    {
        VersionResult = new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate },
        WebInterfaceState = new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true },
        ConfigurationValues = Array.Empty<PrinterConfigurationValue>(),
    };

    // Overrides the merged IPrinterStatusReader read for a specific device - the single Bluetooth
    // read that now drives PrinterVersionAlert/WebInterfaceTogglePanel/CheckConfigurationResults'
    // initial content together (see IPrinterStatusReader's own doc comment).
    private void StubStatus(
        PrinterDevice device,
        PrinterVersionOutcome outcome = PrinterVersionOutcome.UpToDate,
        string? linkOsVersionFound = null,
        string? firmwareVersionFound = null,
        FirmwareBundle? bundle = null,
        bool webInterfaceEnabled = true,
        IReadOnlyList<PrinterConfigurationValue>? configurationValues = null)
    {
        _statusReader.ReadStatusAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
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

    private IRenderedComponent<Pairing> RenderWithReadyPrinter(PrinterDevice device, int? wifiProbePort = null)
    {
        var cut = wifiProbePort is { } port
            ? Render<Pairing>(p => p.Add(c => c.WifiProbePort, port))
            : Render<Pairing>();
        _discoveryService.RaisePrinterDiscovered(device);
        // Ready (and everything gated on it, including this testid) only renders after the
        // automatic WiFi check fully resolves, which can take up to its own probe timeout for an
        // unreachable-port scenario - longer than bUnit's default wait window covers.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='discovered-device']")), TimeSpan.FromSeconds(5));
        return cut;
    }

    // Factory Reset/Recheck Configuration/Push Bag Tag Templates/Calibrate Media/Start Over all now
    // live behind PrinterActionsMenu's overflow menu rather than their own visible buttons - opens it
    // then clicks the given item, matching the real two-tap user flow (bUnit doesn't enforce real
    // visibility rules, so skipping the open click would still technically work, but this stays
    // faithful to what a user actually does).
    private static void OpenActionsMenuAndClickItem(IRenderedComponent<Pairing> cut, string menuItemTestId)
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

    [Fact]
    public void InitialRender_ShowsWaitingInstructionAndStartsListening()
    {
        var cut = Render<Pairing>();

        Assert.Contains("Tap this device to the printer", cut.Markup);
        Assert.Equal(1, _discoveryService.StartListeningCallCount);
    }

    [Fact]
    public void InitialRender_ShowsPairAPrinterFunctionName()
    {
        var cut = Render<Pairing>();

        Assert.Contains("Pair a Printer", cut.Find("h1").TextContent);
    }

    [Fact]
    public void WhenPrinterDiscovered_ShowsNfcBtPairingFunctionNameAndSetsBluetoothConnecting()
    {
        var pairingTcs = new TaskCompletionSource<bool>();
        _pairingService.EnsurePairedHandler = _ => pairingTcs.Task;
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        _discoveryService.RaisePrinterDiscovered(device);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("NFC/BT Pairing", cut.Find("h1").TextContent);
            Assert.Equal(ConnectionIndicatorState.Connecting, _connectivityMonitor.Bluetooth);
        });

        pairingTcs.SetResult(true);
        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Bluetooth));
    }

    [Fact]
    public void WhenPrinterDiscoveredTwiceInQuickSuccession_OnlyAttemptsPairingOnce()
    {
        // Regression test for a real bug: a single NFC tap sometimes dispatched two intents,
        // starting two concurrent CreateBond() attempts against the same printer - visible
        // on-device as two different pairing codes and two OS pairing dialogs.
        var pairingAttempts = 0;
        var pairingTcs = new TaskCompletionSource<bool>();
        _pairingService.EnsurePairedHandler = _ =>
        {
            pairingAttempts++;
            return pairingTcs.Task;
        };
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        _discoveryService.RaisePrinterDiscovered(device);
        _discoveryService.RaisePrinterDiscovered(device);

        cut.WaitForAssertion(() => Assert.Contains("NFC/BT Pairing", cut.Find("h1").TextContent));
        Assert.Equal(1, pairingAttempts);

        pairingTcs.SetResult(true);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='discovered-device']")));
    }

    [Fact]
    public void WhenPairingFails_SetsBluetoothError()
    {
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        _discoveryService.RaisePrinterDiscovered(device);

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Error, _connectivityMonitor.Bluetooth));
    }

    [Fact]
    public void WhenPrinterDiscoveredAndPairingSucceeds_ShowsContinueButton()
    {
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF", SerialNumber = "12345" };

        _discoveryService.RaisePrinterDiscovered(device);

        cut.WaitForAssertion(() =>
        {
            var testIdElement = cut.Find("[data-testid='discovered-device']");
            Assert.Contains("12345", testIdElement.TextContent);
        });
        Assert.NotNull(cut.Find("[data-testid='configure-printer-button']"));
    }

    [Fact]
    public void WhenPairingFails_ShowsErrorWithRetry()
    {
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        _discoveryService.RaisePrinterDiscovered(device);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='pairing-error']")));

        cut.Find("md-filled-button").Click(); // "Try Again"

        Assert.Contains("Tap this device to the printer", cut.Markup);
    }

    [Fact]
    public void ClickingContinue_StoresDeviceInSessionAndNavigatesToConfigure()
    {
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='discovered-device']")));

        cut.Find("[data-testid='configure-printer-button']").Click();

        var session = Services.GetRequiredService<PairingSession>();
        Assert.Same(device, session.Device);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/configure", navigation.Uri);
    }

    [Fact]
    public void ClickingFactoryReset_ShowsConfirmationWarning()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        Assert.NotNull(cut.Find("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public void CancellingFactoryResetConfirmation_ReturnsToReadyWithoutCallingService()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);
        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.NotNull(cut.Find("[data-testid='discovered-device']"));
        Assert.Empty(cut.FindAll("[data-testid='factory-reset-warning']"));
        Assert.Null(_factoryResetService.LastResetMacAddress);
    }

    [Fact]
    public void ReadyState_ShowsRecheckConfigurationMenuItem()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        Assert.NotNull(cut.Find("[data-testid='menu-item-recheck-configuration']"));
    }

    [Theory]
    [InlineData(true, "enabled", "text-success")]
    [InlineData(false, "disabled", "text-danger")]
    public void ReadyState_ShowsWebInterfaceStatusLine_ColoredByEnabledState(bool enabled, string expectedWord, string expectedClass)
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        StubStatus(device, webInterfaceEnabled: enabled);

        var cut = RenderWithReadyPrinter(device);

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
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _statusReader.ReadStatusAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PrinterStatus>(new InvalidOperationException("simulated status check failure")));
        _webInterfaceService.ReadStateAsync(device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
        var cut = RenderWithReadyPrinter(device);
        cut.WaitForAssertion(() => Assert.Contains("Could not check web interface status.", cut.Find("[data-testid='web-interface-error']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='web-interface-status']"));

        cut.FindAll("md-filled-button").First(b => b.TextContent.Trim() == "Try Again").Click();

        cut.WaitForAssertion(() =>
        {
            var statusLine = cut.Find("[data-testid='web-interface-status']");
            Assert.Contains("currently enabled", statusLine.TextContent);
            Assert.Contains("text-success", statusLine.ClassList);
        });
    }

    [Fact]
    public void ReadyState_AutomaticallyPopulatesConfigurationListWithoutClicking()
    {
        // The single merged Bluetooth read now covers the configuration list too, so it appears as
        // soon as the printer is Ready - no click needed (unlike the old per-purpose reads this
        // replaced).
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        StubStatus(device, configurationValues: [new PrinterConfigurationValue("device.friendly_name", "Warehouse-01")]);

        var cut = RenderWithReadyPrinter(device);

        cut.WaitForAssertion(() => Assert.Contains("Warehouse-01", cut.Find("[data-testid='check-configuration-results']").TextContent));
    }

    [Fact]
    public void ClickingRecheckConfiguration_RefreshesFirmwareStatus_WebInterfaceState_AndConfigurationList()
    {
        using var listener = StartLoopbackListener(out var port);
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
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
        _statusReader.ReadStatusAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(firstStatus), rechecktcs.Task);
        var cut = RenderWithReadyPrinter(device, wifiProbePort: port);
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
    public void WhileFactoryResetIsSelected_ConfigurePrinterButtonIsDisabled()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled"));
    }

    [Fact]
    public void WhileWebInterfaceToggleIsApplying_ConfigurePrinterAndCheckConfigurationAreDisabled()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var setEnabledTcs = new TaskCompletionSource();
        _webInterfaceService.SetEnabledAsync(device, false, Arg.Any<CancellationToken>()).Returns(setEnabledTcs.Task);
        var cut = RenderWithReadyPrinter(device);
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
    public void ReadyState_ShowsCalibrateMediaMenuItem()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        Assert.NotNull(cut.Find("[data-testid='menu-item-calibrate-media']"));
    }

    [Fact]
    public void WhileCalibratingMedia_ConfigurePrinterAndCheckConfigurationAndStartOverAreDisabled()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var calibrateTcs = new TaskCompletionSource();
        _calibrationService.CalibrateAsync(device, Arg.Any<CancellationToken>()).Returns(calibrateTcs.Task);
        var cut = RenderWithReadyPrinter(device);
        OpenActionsMenuAndClickItem(cut, "menu-item-calibrate-media");

        cut.Find("[data-testid='calibrate-media-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='menu-item-start-over']").HasAttribute("disabled"));
        });
        calibrateTcs.SetResult();
    }

    [Fact]
    public void WhileFactoryResetIsSelected_StartOverButtonIsDisabled()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        Assert.True(cut.Find("[data-testid='menu-item-start-over']").HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingStartOver_ReturnsToWaitingForTapAndResetsConnectivityMonitorAndConnectionMode()
    {
        // Unlike "Try Again" (only reachable from the Failed state, where nothing is in flight), this
        // is reachable straight from Ready - including while a merged status check is still running -
        // so it needs to work as an actual escape hatch for a printer the user wants to abandon, not
        // just a cosmetic "disabled while busy" control.
        _connectionModeProvider.UseWifi("192.168.1.50");
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        OpenActionsMenuAndClickItem(cut, "menu-item-start-over");

        Assert.Contains("Tap this device to the printer", cut.Markup);
        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
    }

    [Fact]
    public void ClickingStartOver_WhileStatusCheckStillInFlight_LateResultDoesNotResurfaceStaleData()
    {
        // Regression guard for the race this button introduces: LoadPrinterStatusAsync doesn't
        // actually cancel the in-flight Bluetooth read (interrupting a blocking SDK call is out of
        // scope, same as LinkOsPrinterVersionCheckService's own documented choice) - it just needs to
        // discard a late result instead of resurrecting it into whatever's on screen by the time it
        // arrives, which by then could be a totally different (freshly re-tapped) printer.
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var statusTcs = new TaskCompletionSource<PrinterStatus>();
        _statusReader.ReadStatusAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(statusTcs.Task);
        var cut = RenderWithReadyPrinter(device);
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='menu-item-start-over']").HasAttribute("disabled")));

        OpenActionsMenuAndClickItem(cut, "menu-item-start-over");
        Assert.Contains("Tap this device to the printer", cut.Markup);

        statusTcs.SetResult(DefaultPrinterStatus());

        // Nothing should have changed as a result of the now-stale read resolving - still waiting for
        // a fresh tap, not silently jumping back to Ready with the abandoned printer's data.
        Assert.Contains("Tap this device to the printer", cut.Markup);
    }

    [Fact]
    public void AfterCancellingFactoryReset_ConfigurePrinterButtonIsEnabledAgain()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);
        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.False(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void ConfirmingFactoryReset_CallsServiceWithDiscoveredDeviceAndShowsCompletion()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);
        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-complete']")));
        Assert.Equal("AABBCCDDEEFF", _factoryResetService.LastResetMacAddress);
        Assert.Equal("AABBCCDDEEFF", _pairingService.LastRemovedBondMacAddress);
    }

    [Fact]
    public void WhenFactoryResetFails_ShowsErrorAndDoesNotRemoveBond()
    {
        _factoryResetService.ShouldThrow = true;
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);
        OpenActionsMenuAndClickItem(cut, "menu-item-factory-reset");

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-error']")));
        Assert.Null(_pairingService.LastRemovedBondMacAddress);
    }

    [Fact]
    public void ReadyState_ShowsPrinterPairedFunctionName()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        Assert.Contains("Printer Paired", cut.Find("h1").TextContent);
    }

    [Fact]
    public void WhenPairingSucceeds_AndPrinterHasNoWifiConfigured_SetsWifiIndicatorToError()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        var cut = RenderWithReadyPrinter(device);

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Error, _connectivityMonitor.Wifi));
        _wifiMonitor.DidNotReceive().Start(Arg.Any<string>());
    }

    [Fact]
    public void WhenPairingSucceeds_AndPrinterIsReachableOnWifi_SetsWifiIndicatorToConnectedAndStartsMonitor()
    {
        using var listener = StartLoopbackListener(out var port);
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);

        var cut = RenderWithReadyPrinter(device, wifiProbePort: port);

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Wifi));
        _wifiMonitor.Received().Start("127.0.0.1");
    }

    [Fact]
    public void WhenPairingSucceeds_AndPrinterIsNotReachableOnWifi_SetsWifiIndicatorToErrorButStillStartsMonitor()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);

        // A closed loopback port refuses the probe almost instantly, unlike a real unreachable IP
        // which can take the full probe timeout to fail - keeps this test fast and deterministic.
        var cut = RenderWithReadyPrinter(device, wifiProbePort: GetFreeLoopbackPort());

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Error, _connectivityMonitor.Wifi), TimeSpan.FromSeconds(5));
        // Still started so the indicator keeps reflecting live reachability afterward - the printer
        // may still be finishing its own WiFi association right after a reboot/re-tap.
        _wifiMonitor.Received().Start("127.0.0.1");
    }

    [Fact]
    public void WhenVersionCheckIsUpToDate_ConfigurePrinterButtonIsEnabled()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        var cut = RenderWithReadyPrinter(device);

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void WhenVersionCheckNeedsUpdate_AndWifiIsAvailable_ConfigurePrinterButtonIsDisabled()
    {
        using var listener = StartLoopbackListener(out var port);
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        StubStatus(device, outcome: PrinterVersionOutcome.NeedsUpdate, linkOsVersionFound: "7.5.0", firmwareVersionFound: "V93.21.06Z");

        var cut = RenderWithReadyPrinter(device, wifiProbePort: port);

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void ClickingUpdateFirmware_PassesTheRealDiscoveredWifiIpAddress_NotALiteralFieldName()
    {
        // Regression test for a real bug: PrinterVersionAlert's WifiIpAddress="_wifiIpAddress" (no
        // @ prefix) bound the literal text "_wifiIpAddress" instead of the field's value, since a
        // string literal is itself a valid value for a string-typed component parameter - it
        // compiled and looked fine, but sent that literal text to the Zebra SDK's TcpConnection,
        // which then failed to resolve it as a host ("hostname nor servname provided"). This only
        // renders the full page (not the isolated component via the test-harness parameter API,
        // which bypasses Razor attribute parsing entirely and would never have caught this).
        using var listener = StartLoopbackListener(out var port);
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        var bundle = new FirmwareBundle
        {
            ModelName = "ZD421",
            ExpectedLinkOsVersion = new LinkOsVersion(7, 6, 2),
            ExpectedFirmwareVersion = "V93.21.49Z",
            FirmwareAssetLogicalPath = "ZD421_Firmware/V93.21.49Z.zpl",
        };
        StubStatus(device, outcome: PrinterVersionOutcome.NeedsUpdate, bundle: bundle, linkOsVersionFound: "7.5.0", firmwareVersionFound: "V93.21.06Z");
        _firmwareUpdateLauncher.StartAsync(device, bundle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var cut = RenderWithReadyPrinter(device, wifiProbePort: port);
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='update-firmware-button']").Click();

        cut.WaitForAssertion(() =>
            _ = _firmwareUpdateLauncher.Received(1).StartAsync(device, bundle, "127.0.0.1", Arg.Any<CancellationToken>()));
        _ = _firmwareUpdateLauncher.DidNotReceive().StartAsync(device, bundle, "_wifiIpAddress", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WhenVersionCheckNeedsUpdate_AndNoWifiIsConfiguredYet_ConfigurePrinterButtonStaysEnabled()
    {
        // A never-configured printer has no WiFi yet, and "Configure Printer" is exactly what gives
        // it one - blocking here would deadlock (Configure blocked pending an update that itself
        // requires WiFi Configure hasn't set up yet). Result.razor re-surfaces this same check once
        // the printer actually has WiFi, after a successful configuration.
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        StubStatus(device, outcome: PrinterVersionOutcome.NeedsUpdate, linkOsVersionFound: "7.5.0", firmwareVersionFound: "V93.21.06Z");

        var cut = RenderWithReadyPrinter(device);

        Assert.False(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void WhenVersionCheckIsUnsupported_ConfigurePrinterButtonIsDisabled_UntilSkipped()
    {
        // The version check only runs once WiFi is available (a firmware update can only ever happen
        // over WiFi, so there's no point checking before then) - simulates a re-tap of a printer
        // that's already configured and reachable, not a fresh/never-configured one.
        using var listener = StartLoopbackListener(out var port);
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        StubStatus(device, outcome: PrinterVersionOutcome.Unsupported);
        var cut = RenderWithReadyPrinter(device, wifiProbePort: port);
        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='version-check-skip']").Click();

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='configure-printer-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void WhenVersionCheckIsUnsupported_ClickingCancel_ReturnsToWaitingForTap()
    {
        using var listener = StartLoopbackListener(out var port);
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        StubStatus(device, outcome: PrinterVersionOutcome.Unsupported);
        var cut = RenderWithReadyPrinter(device, wifiProbePort: port);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-cancel']")));

        cut.Find("[data-testid='version-check-cancel']").Click();

        Assert.Contains("Tap this device to the printer", cut.Markup);
    }

    [Fact]
    public void ClickingTryAgainAfterPairingFailure_ResetsConnectivityMonitorAndConnectionModeAndStopsWifiMonitor()
    {
        _connectionModeProvider.UseWifi("192.168.1.50");
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='pairing-error']")));

        cut.Find("md-filled-button").Click(); // "Try Again"

        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
        _wifiMonitor.Received().Stop();
    }

    [Fact]
    public void WhenSessionAlreadyHasDevice_ShowsReadyImmediately_WithoutStartingDiscovery()
    {
        // Simulates returning here via Configure.razor's Back button - the printer is already
        // paired, so this should show it directly rather than restarting discovery and forcing a
        // re-tap.
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF", SerialNumber = "12345" };
        Services.AddSingleton(new PairingSession { Device = device });

        var cut = Render<Pairing>();

        cut.WaitForAssertion(() => Assert.Contains("12345", cut.Find("[data-testid='discovered-device']").TextContent));
        Assert.NotNull(cut.Find("[data-testid='configure-printer-button']"));
        Assert.Equal(0, _discoveryService.StartListeningCallCount);
        Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Bluetooth);
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        var cut = Render<Pairing>();

        cut.Instance.Dispose();

        Assert.Equal(1, _discoveryService.StopListeningCallCount);
    }

    private sealed class FakePrinterDiscoveryService : IPrinterDiscoveryService
    {
        public int StartListeningCallCount { get; private set; }

        public int StopListeningCallCount { get; private set; }

        public event EventHandler<PrinterDevice>? PrinterDiscovered;

        public void StartListening() => StartListeningCallCount++;

        public void StopListening() => StopListeningCallCount++;

        public void RaisePrinterDiscovered(PrinterDevice device) => PrinterDiscovered?.Invoke(this, device);
    }

    private sealed class FakeBluetoothPairingService : IBluetoothPairingService
    {
        public Func<string, Task<bool>> EnsurePairedHandler { get; set; } = _ => Task.FromResult(true);

        public string? LastRemovedBondMacAddress { get; private set; }

        public Task<bool> EnsurePairedAsync(string macAddress, CancellationToken cancellationToken = default) =>
            EnsurePairedHandler(macAddress);

        public Task RemoveBondAsync(string macAddress, CancellationToken cancellationToken = default)
        {
            LastRemovedBondMacAddress = macAddress;
            return Task.CompletedTask;
        }
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
