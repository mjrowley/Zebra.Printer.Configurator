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
    private readonly PrinterConnectivityMonitor _connectivityMonitor = new();
    private readonly IWifiConnectivityMonitor _wifiMonitor = Substitute.For<IWifiConnectivityMonitor>();

    public PairingTests()
    {
        Services.AddSingleton<IPrinterDiscoveryService>(_discoveryService);
        Services.AddSingleton<IBluetoothPairingService>(_pairingService);
        Services.AddSingleton<IPrinterFactoryResetService>(_factoryResetService);
        Services.AddSingleton(_configurationReader);
        Services.AddSingleton(_connectivityMonitor);
        Services.AddSingleton(_wifiMonitor);
        Services.AddSingleton(new PairingSession());
    }

    private IRenderedComponent<Pairing> RenderWithReadyPrinter(PrinterDevice device)
    {
        var cut = Render<Pairing>();
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='discovered-device']")));
        return cut;
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
    public void ClickingTryAgainAfterPairingFailure_ResetsConnectivityMonitorAndStopsWifiMonitor()
    {
        _pairingService.EnsurePairedHandler = _ => Task.FromResult(false);
        var cut = Render<Pairing>();
        var device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" };
        _discoveryService.RaisePrinterDiscovered(device);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='pairing-error']")));

        cut.Find("button").Click(); // "Try Again"

        Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
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
