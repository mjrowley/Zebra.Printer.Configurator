using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Pages;

namespace Zebra.Printer.Configurator.ComponentTests.Pages;

public class PairingTests : BunitContext
{
    private readonly FakePrinterDiscoveryService _discoveryService = new();
    private readonly FakeBluetoothPairingService _pairingService = new();
    private readonly FakePrinterFactoryResetService _factoryResetService = new();

    public PairingTests()
    {
        Services.AddSingleton<IPrinterDiscoveryService>(_discoveryService);
        Services.AddSingleton<IBluetoothPairingService>(_pairingService);
        Services.AddSingleton<IPrinterFactoryResetService>(_factoryResetService);
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
