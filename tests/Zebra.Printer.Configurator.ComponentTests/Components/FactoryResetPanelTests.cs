using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class FactoryResetPanelTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IPrinterFactoryResetService _factoryResetService = Substitute.For<IPrinterFactoryResetService>();
    private readonly IBluetoothPairingService _pairingService = Substitute.For<IBluetoothPairingService>();

    public FactoryResetPanelTests()
    {
        Services.AddSingleton(_factoryResetService);
        Services.AddSingleton(_pairingService);
    }

    [Fact]
    public void InitialRender_ShowsFactoryResetButtonOnly()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));

        Assert.NotNull(cut.Find("[data-testid='factory-reset-button']"));
        Assert.Empty(cut.FindAll("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public void ClickingFactoryReset_ShowsConfirmationWarning()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));

        cut.Find("[data-testid='factory-reset-button']").Click();

        Assert.NotNull(cut.Find("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public void CancellingConfirmation_ReturnsToIdle()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));
        cut.Find("[data-testid='factory-reset-button']").Click();

        cut.Find("[data-testid='factory-reset-cancel']").Click();

        Assert.NotNull(cut.Find("[data-testid='factory-reset-button']"));
        Assert.Empty(cut.FindAll("[data-testid='factory-reset-warning']"));
    }

    [Fact]
    public async Task ConfirmingReset_CallsFactoryResetThenRemovesBondAndShowsCompletion()
    {
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));
        cut.Find("[data-testid='factory-reset-button']").Click();

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-complete']")));
        await _factoryResetService.Received(1).ResetToFactoryDefaultsAsync(Device, Arg.Any<CancellationToken>());
        await _pairingService.Received(1).RemoveBondAsync(Device.BluetoothMacAddress, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WhenResetFails_ShowsErrorAndDoesNotRemoveBond()
    {
        _factoryResetService.ResetToFactoryDefaultsAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated failure")));
        var cut = Render<FactoryResetPanel>(p => p.Add(c => c.Device, Device));
        cut.Find("[data-testid='factory-reset-button']").Click();

        cut.Find("[data-testid='factory-reset-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-error']")));
        _pairingService.DidNotReceive().RemoveBondAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingStartOverAfterCompletion_RaisesOnFinished()
    {
        var finishedRaised = false;
        var cut = Render<FactoryResetPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.OnFinished, () => finishedRaised = true));
        cut.Find("[data-testid='factory-reset-button']").Click();
        cut.Find("[data-testid='factory-reset-confirm']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='factory-reset-complete']")));

        cut.Find("button").Click();

        Assert.True(finishedRaised);
    }
}
