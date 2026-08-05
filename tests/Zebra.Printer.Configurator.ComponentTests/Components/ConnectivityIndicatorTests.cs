using Bunit;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class ConnectivityIndicatorTests : BunitContext
{
    [Theory]
    [InlineData(ConnectionIndicatorState.Disconnected, "state-disconnected")]
    [InlineData(ConnectionIndicatorState.Connecting, "state-connecting")]
    [InlineData(ConnectionIndicatorState.Error, "state-error")]
    [InlineData(ConnectionIndicatorState.Connected, "state-connected")]
    public void Render_AppliesClassMatchingState(ConnectionIndicatorState state, string expectedClass)
    {
        var cut = Render<ConnectivityIndicator>(p => p
            .Add(c => c.Kind, IndicatorKind.Bluetooth)
            .Add(c => c.State, state));

        Assert.Contains(expectedClass, cut.Find("[data-testid='bluetooth-indicator']").ClassList);
    }

    [Fact]
    public void Render_Bluetooth_UsesBluetoothTestId()
    {
        var cut = Render<ConnectivityIndicator>(p => p
            .Add(c => c.Kind, IndicatorKind.Bluetooth)
            .Add(c => c.State, ConnectionIndicatorState.Connected));

        Assert.NotNull(cut.Find("[data-testid='bluetooth-indicator']"));
    }

    [Fact]
    public void Render_Wifi_UsesWifiTestId()
    {
        var cut = Render<ConnectivityIndicator>(p => p
            .Add(c => c.Kind, IndicatorKind.Wifi)
            .Add(c => c.State, ConnectionIndicatorState.Connected));

        Assert.NotNull(cut.Find("[data-testid='wifi-indicator']"));
    }

    [Fact]
    public void Render_TitleIncludesKindAndState()
    {
        var cut = Render<ConnectivityIndicator>(p => p
            .Add(c => c.Kind, IndicatorKind.Wifi)
            .Add(c => c.State, ConnectionIndicatorState.Error));

        Assert.Equal("WiFi: Error", cut.Find("[data-testid='wifi-indicator']").GetAttribute("title"));
    }
}
