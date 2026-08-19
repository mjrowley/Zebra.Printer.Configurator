using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Pages;

namespace Zebra.Printer.Configurator.ComponentTests.Pages;

// Covers only WaitingForTap/Pairing/Failed and the handoff to PrinterDashboard once pairing succeeds
// or an already-paired session is restored - everything about what the printer looks like once
// paired (firmware status, web interface, actions menu, WiFi discovery, ...) moved to
// PrinterDashboardTests.cs along with the "Ready" state itself (see PrinterDashboard.razor's own doc
// comment on why that state no longer lives on this page).
public class PairingTests : BunitContext
{
    private readonly FakePrinterDiscoveryService _discoveryService = new();
    private readonly FakeBluetoothPairingService _pairingService = new();
    private readonly IAppLog _appLog = Substitute.For<IAppLog>();
    private readonly PrinterConnectivityMonitor _connectivityMonitor = new();
    private readonly IWifiConnectivityMonitor _wifiMonitor = Substitute.For<IWifiConnectivityMonitor>();
    private readonly PrinterConnectionModeProvider _connectionModeProvider = new();

    public PairingTests()
    {
        Services.AddSingleton<IPrinterDiscoveryService>(_discoveryService);
        Services.AddSingleton<IBluetoothPairingService>(_pairingService);
        Services.AddSingleton(_appLog);
        Services.AddSingleton(_connectivityMonitor);
        Services.AddSingleton(_wifiMonitor);
        Services.AddSingleton<IPrinterConnectionModeProvider>(_connectionModeProvider);
        Services.AddSingleton(new PairingSession());
    }

    // Raises discovery, waits for the resulting NFC Tap Received dialog, then dismisses it via
    // Continue - the shared "get past the dialog" step every test below that actually needs pairing
    // to start uses, mirroring the real two-step user flow (tap, then confirm) rather than assuming
    // pairing starts the instant a printer is discovered.
    private static void DiscoverAndContinue(IRenderedComponent<Pairing> cut, FakePrinterDiscoveryService discoveryService, PrinterDevice device)
    {
        discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='nfc-tap-received-dialog']")));
        cut.Find("[data-testid='nfc-tap-received-continue']").Click();
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
    public void WhenPrinterDiscovered_ShowsNfcTapReceivedDialog_WithoutStartingPairingYet()
    {
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        _discoveryService.RaisePrinterDiscovered(device);

        cut.WaitForAssertion(() =>
        {
            var dialog = cut.Find("[data-testid='nfc-tap-received-dialog']");
            Assert.Contains("move your device away from the printer", dialog.TextContent);
        });
        // Still waiting - the actual Bluetooth pairing negotiation hasn't started yet, so the
        // function name/indicator haven't changed from their WaitingForTap values.
        Assert.Contains("Pair a Printer", cut.Find("h1").TextContent);
        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
    }

    [Fact]
    public void WhenSecondTapArrivesWhileDialogIsShowing_IsIgnored()
    {
        // Same duplicate-intent regression this app has already hit once (a single physical NFC tap
        // dispatching more than one intent) - now needs guarding at the dialog stage too, before
        // pairing itself has even started.
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='nfc-tap-received-dialog']")));

        _discoveryService.RaisePrinterDiscovered(device);

        Assert.Single(cut.FindAll("[data-testid='nfc-tap-received-dialog']"));
    }

    [Fact]
    public void ClickingContinue_StartsPairingAndShowsNfcBtPairingFunctionName()
    {
        var pairingTcs = new TaskCompletionSource<bool>();
        _pairingService.EnsurePairedHandler = _ => pairingTcs.Task;
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='nfc-tap-received-dialog']")));

        cut.Find("[data-testid='nfc-tap-received-continue']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='nfc-tap-received-dialog']"));
            Assert.Contains("NFC/BT Pairing", cut.Find("h1").TextContent);
            Assert.Equal(ConnectionIndicatorState.Connecting, _connectivityMonitor.Bluetooth);
        });

        pairingTcs.SetResult(true);
        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Bluetooth));
    }

    [Fact]
    public void WhenPrinterDiscoveredTwiceInQuickSuccessionAfterContinue_OnlyAttemptsPairingOnce()
    {
        // Regression test for a real bug: a single NFC tap sometimes dispatched two intents,
        // starting two concurrent CreateBond() attempts against the same printer - visible
        // on-device as two different pairing codes and two OS pairing dialogs. Covers the case
        // where the duplicate arrives after Continue (during the actual pairing negotiation) -
        // WhenSecondTapArrivesWhileDialogIsShowing_IsIgnored covers the earlier window.
        var pairingAttempts = 0;
        var pairingTcs = new TaskCompletionSource<bool>();
        _pairingService.EnsurePairedHandler = _ =>
        {
            pairingAttempts++;
            return pairingTcs.Task;
        };
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        DiscoverAndContinue(cut, _discoveryService, device);
        cut.WaitForAssertion(() => Assert.Contains("NFC/BT Pairing", cut.Find("h1").TextContent));

        _discoveryService.RaisePrinterDiscovered(device);

        Assert.Equal(1, pairingAttempts);

        pairingTcs.SetResult(true);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/dashboard", navigation.Uri));
    }

    [Fact]
    public void WhenPairingFails_SetsBluetoothError()
    {
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        DiscoverAndContinue(cut, _discoveryService, device);

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Error, _connectivityMonitor.Bluetooth));
    }

    [Fact]
    public void WhenPrinterDiscoveredAndPairingSucceeds_StoresDeviceInSessionAndNavigatesToDashboard()
    {
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF", SerialNumber = "12345" };

        DiscoverAndContinue(cut, _discoveryService, device);

        var session = Services.GetRequiredService<PairingSession>();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        cut.WaitForAssertion(() =>
        {
            Assert.Same(device, session.Device);
            Assert.EndsWith("/dashboard", navigation.Uri);
        });
    }

    [Fact]
    public void WhenPairingFails_ShowsErrorWithRetry()
    {
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        DiscoverAndContinue(cut, _discoveryService, device);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='pairing-error']")));

        cut.Find("md-filled-button").Click(); // "Try Again"

        Assert.Contains("Tap this device to the printer", cut.Markup);
    }

    [Fact]
    public void ClickingTryAgainAfterPairingFailure_ResetsConnectivityMonitorAndConnectionModeAndStopsWifiMonitor()
    {
        _connectionModeProvider.UseWifi("192.168.1.50");
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        DiscoverAndContinue(cut, _discoveryService, device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='pairing-error']")));

        cut.Find("md-filled-button").Click(); // "Try Again"

        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
        _wifiMonitor.Received().Stop();
    }

    [Fact]
    public void WhenSessionAlreadyHasDevice_NavigatesToDashboardImmediately_WithoutStartingDiscovery()
    {
        // Simulates returning here via Configure.razor's Back button - the printer is already
        // paired, so this should go straight to the dashboard rather than restarting discovery and
        // forcing a re-tap.
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF", SerialNumber = "12345" };
        Services.AddSingleton(new PairingSession { Device = device });

        Render<Pairing>();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/dashboard", navigation.Uri);
        Assert.Equal(0, _discoveryService.StartListeningCallCount);
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
}
