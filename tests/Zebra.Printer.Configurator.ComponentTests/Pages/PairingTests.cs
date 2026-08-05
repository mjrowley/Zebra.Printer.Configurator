using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
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
        Services.AddSingleton(new PairingSession());

        // Unconfigured, this NSubstitute mock resolves ReadConfigurationAsync's Task with a null
        // result by default, which the automatic post-pairing WiFi check would then throw on when
        // reading .FirstOrDefault() from it - most tests here don't care about that check at all, so
        // give it a harmless empty result unless a specific test overrides this setup itself.
        _configurationReader.ReadConfigurationAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PrinterConfigurationValue>());
    }

    private IRenderedComponent<Pairing> RenderWithReadyPrinter(PrinterDevice device, int? wifiProbePort = null)
    {
        var cut = wifiProbePort is { } port
            ? Render<Pairing>(p => p.Add(c => c.WifiProbePort, port))
            : Render<Pairing>();
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='discovered-device']")));
        return cut;
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

        Assert.Contains("Tap your Zebra printer", cut.Markup);
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
        Assert.NotNull(cut.Find("button"));
    }

    [Fact]
    public void WhenPairingRequiresCodeConfirmation_ShowsCodeAndConfirmingItProceedsToReady()
    {
        _pairingService.EnsurePairedHandler = async _ =>
        {
            var args = new PairingCodeRequestedEventArgs("123456");
            _pairingService.RaisePairingCodeRequested(args);
            return await args.Response.Task;
        };
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        _discoveryService.RaisePrinterDiscovered(device);

        cut.WaitForAssertion(() => Assert.Contains("123456", cut.Find("[data-testid='pairing-code']").TextContent));

        cut.Find("button").Click(); // "Pair"

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='discovered-device']")));
    }

    [Fact]
    public void WhenPairingFails_ShowsErrorWithRetry()
    {
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };

        _discoveryService.RaisePrinterDiscovered(device);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='pairing-error']")));

        cut.Find("button").Click(); // "Try Again"

        Assert.Contains("Tap your Zebra printer", cut.Markup);
    }

    [Fact]
    public void ClickingContinue_StoresDeviceInSessionAndNavigatesToConfigure()
    {
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='discovered-device']")));

        cut.Find("button").Click();

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

        cut.Find("[data-testid='factory-reset-button']").Click();

        Assert.NotNull(cut.Find("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public void CancellingFactoryResetConfirmation_ReturnsToReadyWithoutCallingService()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);
        cut.Find("[data-testid='factory-reset-button']").Click();

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.NotNull(cut.Find("[data-testid='discovered-device']"));
        Assert.Empty(cut.FindAll("[data-testid='factory-reset-warning']"));
        Assert.Null(_factoryResetService.LastResetMacAddress);
    }

    [Fact]
    public void ReadyState_ShowsCheckConfigurationButton()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        Assert.NotNull(cut.Find("[data-testid='check-configuration-button']"));
    }

    [Fact]
    public void WhileFactoryResetIsSelected_ConfigurePrinterButtonIsDisabled()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);

        cut.Find("[data-testid='factory-reset-button']").Click();

        Assert.True(cut.Find("button").HasAttribute("disabled")); // "Configure Printer" - first button
        Assert.True(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void AfterCancellingFactoryReset_ConfigurePrinterButtonIsEnabledAgain()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);
        cut.Find("[data-testid='factory-reset-button']").Click();

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.False(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void ConfirmingFactoryReset_CallsServiceWithDiscoveredDeviceAndShowsCompletion()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        var cut = RenderWithReadyPrinter(device);
        cut.Find("[data-testid='factory-reset-button']").Click();

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
        cut.Find("[data-testid='factory-reset-button']").Click();

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
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);

        var cut = RenderWithReadyPrinter(device, wifiProbePort: port);

        cut.WaitForAssertion(() => Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Wifi));
        _wifiMonitor.Received().Start("127.0.0.1");
    }

    [Fact]
    public void WhenPairingSucceeds_AndPrinterIsNotReachableOnWifi_SetsWifiIndicatorToErrorButStillStartsMonitor()
    {
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _configurationReader.ReadConfigurationAsync(device, Arg.Any<CancellationToken>())
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
    public void ClickingTryAgainAfterPairingFailure_ResetsConnectivityMonitorAndConnectionModeAndStopsWifiMonitor()
    {
        _connectionModeProvider.UseWifi("192.168.1.50");
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='pairing-error']")));

        cut.Find("button").Click(); // "Try Again"

        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
        _wifiMonitor.Received().Stop();
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

        public event EventHandler<PairingCodeRequestedEventArgs>? PairingCodeRequested;

        public Task<bool> EnsurePairedAsync(string macAddress, CancellationToken cancellationToken = default) =>
            EnsurePairedHandler(macAddress);

        public Task RemoveBondAsync(string macAddress, CancellationToken cancellationToken = default)
        {
            LastRemovedBondMacAddress = macAddress;
            return Task.CompletedTask;
        }

        public void RaisePairingCodeRequested(PairingCodeRequestedEventArgs args) => PairingCodeRequested?.Invoke(this, args);
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
