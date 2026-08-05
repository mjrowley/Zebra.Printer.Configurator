using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class CheckConfigurationButtonTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IPrinterConfigurationReader _configurationReader = Substitute.For<IPrinterConfigurationReader>();

    public CheckConfigurationButtonTests()
    {
        Services.AddSingleton(_configurationReader);
    }

    [Fact]
    public void InitialRender_ShowsEnabledButton()
    {
        var cut = Render<CheckConfigurationButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.State, new CheckConfigurationState()));

        Assert.False(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingButton_PopulatesSharedStateWithResults()
    {
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.essid", "Warehouse-WiFi")]);
        var state = new CheckConfigurationState();
        var cut = Render<CheckConfigurationButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.State, state));

        cut.Find("[data-testid='check-configuration-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(state.Values);
            Assert.Equal("wlan.essid", state.Values![0].Key);
            Assert.Equal("Warehouse-WiFi", state.Values[0].Value);
        });
    }

    [Fact]
    public void WhenReadFails_PopulatesSharedStateWithError()
    {
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<PrinterConfigurationValue>>(new InvalidOperationException("simulated failure")));
        var state = new CheckConfigurationState();
        var cut = Render<CheckConfigurationButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.State, state));

        cut.Find("[data-testid='check-configuration-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(state.ErrorMessage);
            Assert.Null(state.Values);
        });
    }

    [Fact]
    public void WhenDisabledParameterIsTrue_ButtonHasDisabledAttribute()
    {
        var cut = Render<CheckConfigurationButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.State, new CheckConfigurationState())
            .Add(c => c.Disabled, true));

        Assert.True(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
    }
}
