using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class FactoryResetPanelTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IPrinterFactoryResetService _factoryResetService = Substitute.For<IPrinterFactoryResetService>();
    private readonly IBluetoothPairingService _pairingService = Substitute.For<IBluetoothPairingService>();
    private readonly PrinterActivityMonitor _activityMonitor = new();

    public FactoryResetPanelTests()
    {
        Services.AddSingleton(_factoryResetService);
        Services.AddSingleton(_pairingService);
        Services.AddSingleton(_activityMonitor);
    }

    [Fact]
    public void InitialRender_ShowsNothing()
    {
        // The trigger now lives in the host page's PrinterActionsMenu overflow menu (RequestConfirmAsync,
        // called via @ref) - this component only renders once actually triggered.
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));

        Assert.Empty(cut.FindAll("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public async Task RequestConfirmAsync_ShowsConfirmationWarning()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));

        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        Assert.NotNull(cut.Find("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public async Task CancellingConfirmation_ReturnsToIdle()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.Empty(cut.FindAll("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public async Task ConfirmingReset_CallsFactoryResetThenRemovesBondAndShowsCompletion()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-complete']")));
        // Forces acknowledgment rather than leaving the message inline alongside other buttons -
        // shown as a modal dialog, not inline text.
        Assert.NotNull(cut.Find("[data-testid='factory-reset-complete-dialog']"));
        await _factoryResetService.Received(1).ResetToFactoryDefaultsAsync(Device, Arg.Any<CancellationToken>());
        await _pairingService.Received(1).RemoveBondAsync(Device.BluetoothMacAddress, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenResetFails_ShowsErrorAndDoesNotRemoveBond()
    {
        _factoryResetService.ResetToFactoryDefaultsAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated failure")));
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-error']")));
        _ = _pairingService.DidNotReceive().RemoveBondAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingCloseAfterCompletion_RaisesOnFinished()
    {
        var finishedRaised = false;
        var cut = Render<FactoryResetPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.OnFinished, () => finishedRaised = true));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());
        cut.Find("[data-testid='factory-reset-confirm']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-complete']")));

        cut.Find("md-filled-button").Click();

        Assert.True(finishedRaised);
    }

    [Fact]
    public async Task RequestConfirmAsync_RaisesIsActiveChangedTrue()
    {
        var activeStates = new List<bool>();
        var cut = Render<FactoryResetPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));

        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        Assert.Equal([true], activeStates);
    }

    [Fact]
    public async Task CancellingConfirmation_RaisesIsActiveChangedFalse()
    {
        var activeStates = new List<bool>();
        var cut = Render<FactoryResetPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.Equal([true, false], activeStates);
    }

    [Fact]
    public async Task ConfirmingReset_StaysActiveThroughCompletion()
    {
        var activeStates = new List<bool>();
        var cut = Render<FactoryResetPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-complete']")));
        // Only one "became active" transition (Idle -> Confirming) - Resetting and Complete are
        // still non-idle, so no further IsActiveChanged events fire until something returns to Idle.
        Assert.Equal([true], activeStates);
    }

    [Fact]
    public async Task RequestConfirmAsync_MarksActivityMonitorBusy()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));

        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        Assert.True(_activityMonitor.IsBusy);
    }

    [Fact]
    public async Task CancellingConfirmation_ClearsActivityMonitorBusy()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.False(_activityMonitor.IsBusy);
    }
}
