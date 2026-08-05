using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;

namespace Zebra.Printer.Configurator.UnitTests.Connectivity;

public class PrinterConnectionModeProviderTests
{
    [Fact]
    public void InitialState_IsBluetoothWithNoWifiAddress()
    {
        var provider = new PrinterConnectionModeProvider();

        Assert.Equal(PrinterConnectionMode.Bluetooth, provider.Mode);
        Assert.Null(provider.WifiIpAddress);
    }

    [Fact]
    public void UseWifi_SetsModeAndAddress()
    {
        var provider = new PrinterConnectionModeProvider();

        provider.UseWifi("192.168.1.50");

        Assert.Equal(PrinterConnectionMode.Wifi, provider.Mode);
        Assert.Equal("192.168.1.50", provider.WifiIpAddress);
    }

    [Fact]
    public void UseBluetooth_AfterUseWifi_ClearsAddress()
    {
        var provider = new PrinterConnectionModeProvider();
        provider.UseWifi("192.168.1.50");

        provider.UseBluetooth();

        Assert.Equal(PrinterConnectionMode.Bluetooth, provider.Mode);
        Assert.Null(provider.WifiIpAddress);
    }

    [Fact]
    public void UseWifi_RaisesChanged()
    {
        var provider = new PrinterConnectionModeProvider();
        var raised = 0;
        provider.Changed += (_, _) => raised++;

        provider.UseWifi("192.168.1.50");

        Assert.Equal(1, raised);
    }

    [Fact]
    public void UseBluetooth_RaisesChanged()
    {
        var provider = new PrinterConnectionModeProvider();
        provider.UseWifi("192.168.1.50");
        var raised = 0;
        provider.Changed += (_, _) => raised++;

        provider.UseBluetooth();

        Assert.Equal(1, raised);
    }
}
