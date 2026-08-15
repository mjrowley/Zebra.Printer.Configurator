using Bunit;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class CheckConfigurationButtonTests : BunitContext
{
    [Fact]
    public void InitialRender_ShowsEnabledButton()
    {
        var cut = Render<CheckConfigurationButton>(p => p
            .Add(c => c.State, new CheckConfigurationState()));

        Assert.False(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingButton_InvokesOnRecheck()
    {
        var invoked = false;
        var cut = Render<CheckConfigurationButton>(p => p
            .Add(c => c.State, new CheckConfigurationState())
            .Add(c => c.OnRecheck, () => invoked = true));

        cut.Find("[data-testid='check-configuration-button']").Click();

        Assert.True(invoked);
    }

    [Fact]
    public void WhenDisabledParameterIsTrue_ButtonHasDisabledAttribute()
    {
        var cut = Render<CheckConfigurationButton>(p => p
            .Add(c => c.State, new CheckConfigurationState())
            .Add(c => c.Disabled, true));

        Assert.True(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void WhenStateIsLoading_ButtonHasDisabledAttribute_AndNoSpinner()
    {
        // PrinterVersionAlert already shows one "Checking printer configuration..." spinner for the
        // whole merged read - a second one on this button would be redundant, so this button only
        // reflects the loading state via its disabled attribute now.
        var state = new CheckConfigurationState();
        state.SetLoading();
        var cut = Render<CheckConfigurationButton>(p => p
            .Add(c => c.State, state));

        Assert.True(cut.Find("[data-testid='check-configuration-button']").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll(".spinner-border"));
    }
}
