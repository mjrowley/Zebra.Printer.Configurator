using Zebra.Printer.Configurator.Core.Connectivity;

namespace Zebra.Printer.Configurator.UnitTests.Connectivity;

public class PrinterConnectivityMonitorTests
{
    [Fact]
    public void InitialState_IsDisconnectedForBothIndicators()
    {
        var monitor = new PrinterConnectivityMonitor();

        Assert.Equal(ConnectionIndicatorState.Disconnected, monitor.Bluetooth);
        Assert.Equal(ConnectionIndicatorState.Disconnected, monitor.Wifi);
    }

    [Fact]
    public void SetBluetooth_UpdatesBluetoothOnly()
    {
        var monitor = new PrinterConnectivityMonitor();

        monitor.SetBluetooth(ConnectionIndicatorState.Connected);

        Assert.Equal(ConnectionIndicatorState.Connected, monitor.Bluetooth);
        Assert.Equal(ConnectionIndicatorState.Disconnected, monitor.Wifi);
    }

    [Fact]
    public void SetWifi_UpdatesWifiOnly()
    {
        var monitor = new PrinterConnectivityMonitor();

        monitor.SetWifi(ConnectionIndicatorState.Error);

        Assert.Equal(ConnectionIndicatorState.Error, monitor.Wifi);
        Assert.Equal(ConnectionIndicatorState.Disconnected, monitor.Bluetooth);
    }

    [Fact]
    public void SetBluetooth_ToADifferentValue_RaisesChanged()
    {
        var monitor = new PrinterConnectivityMonitor();
        var raised = 0;
        monitor.Changed += (_, _) => raised++;

        monitor.SetBluetooth(ConnectionIndicatorState.Connecting);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetBluetooth_ToTheSameValue_DoesNotRaiseChanged()
    {
        var monitor = new PrinterConnectivityMonitor();
        monitor.SetBluetooth(ConnectionIndicatorState.Connecting);
        var raised = 0;
        monitor.Changed += (_, _) => raised++;

        monitor.SetBluetooth(ConnectionIndicatorState.Connecting);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Reset_SetsBothIndicatorsBackToDisconnected()
    {
        var monitor = new PrinterConnectivityMonitor();
        monitor.SetBluetooth(ConnectionIndicatorState.Connected);
        monitor.SetWifi(ConnectionIndicatorState.Connected);

        monitor.Reset();

        Assert.Equal(ConnectionIndicatorState.Disconnected, monitor.Bluetooth);
        Assert.Equal(ConnectionIndicatorState.Disconnected, monitor.Wifi);
    }
}
