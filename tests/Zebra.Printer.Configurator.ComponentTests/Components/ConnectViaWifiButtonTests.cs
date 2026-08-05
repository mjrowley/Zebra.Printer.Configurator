using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class ConnectViaWifiButtonTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IPrinterConfigurationReader _configurationReader = Substitute.For<IPrinterConfigurationReader>();
    private readonly PrinterConnectivityMonitor _connectivityMonitor = new();
    private readonly PrinterConnectionModeProvider _connectionModeProvider = new();
    private readonly IWifiConnectivityMonitor _wifiMonitor = Substitute.For<IWifiConnectivityMonitor>();

    public ConnectViaWifiButtonTests()
    {
        Services.AddSingleton(_configurationReader);
        Services.AddSingleton(_connectivityMonitor);
        Services.AddSingleton<IPrinterConnectionModeProvider>(_connectionModeProvider);
        Services.AddSingleton(_wifiMonitor);
    }

    [Fact]
    public void WhenBluetoothIsNotConnected_ButtonIsHidden()
    {
        var cut = Render<ConnectViaWifiButton>(p => p.Add(c => c.Device, Device));

        Assert.Empty(cut.FindAll("[data-testid='connect-via-wifi-button']"));
    }

    [Fact]
    public void WhenBluetoothIsConnected_ButtonIsShown()
    {
        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);

        var cut = Render<ConnectViaWifiButton>(p => p.Add(c => c.Device, Device));

        Assert.NotNull(cut.Find("[data-testid='connect-via-wifi-button']"));
    }

    [Fact]
    public void WhenAlreadyOnWifi_ButtonIsHidden()
    {
        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);
        _connectionModeProvider.UseWifi("192.168.1.50");

        var cut = Render<ConnectViaWifiButton>(p => p.Add(c => c.Device, Device));

        Assert.Empty(cut.FindAll("[data-testid='connect-via-wifi-button']"));
    }

    [Fact]
    public void ClickingButton_WhenPrinterIsReachable_SwitchesToWifiAndDisconnectsBluetooth()
    {
        using var listener = StartLoopbackListener(out var port);
        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        var cut = Render<ConnectViaWifiButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.Port, port));

        cut.Find("[data-testid='connect-via-wifi-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(PrinterConnectionMode.Wifi, _connectionModeProvider.Mode);
            Assert.Equal("127.0.0.1", _connectionModeProvider.WifiIpAddress);
            Assert.Equal(ConnectionIndicatorState.Disconnected, _connectivityMonitor.Bluetooth);
            Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Wifi);
        });
        _wifiMonitor.Received().Start("127.0.0.1");
    }

    [Fact]
    public void ClickingButton_WhenPrinterIsUnreachable_ReportsErrorAndStaysOnBluetooth()
    {
        var port = GetFreeLoopbackPort();
        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "127.0.0.1")]);
        var cut = Render<ConnectViaWifiButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.Port, port));

        cut.Find("[data-testid='connect-via-wifi-button']").Click();

        // The component's own reachability probe has a 3s timeout (matches production), so this
        // needs longer than bUnit's default WaitForAssertion window to resolve.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='connect-via-wifi-error']")), TimeSpan.FromSeconds(5));
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
        Assert.Equal(ConnectionIndicatorState.Connected, _connectivityMonitor.Bluetooth);
        _wifiMonitor.DidNotReceive().Start(Arg.Any<string>());
    }

    [Fact]
    public void ClickingButton_WhenPrinterHasNoWifiAddress_ReportsErrorAndStaysOnBluetooth()
    {
        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);
        _configurationReader.ReadConfigurationAsync(Device, Arg.Any<CancellationToken>())
            .Returns([new PrinterConfigurationValue("wlan.ip.addr", "0.0.0.0")]);
        var cut = Render<ConnectViaWifiButton>(p => p.Add(c => c.Device, Device));

        cut.Find("[data-testid='connect-via-wifi-button']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='connect-via-wifi-error']")));
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
    }

    [Fact]
    public void WhenDisabledParameterIsTrue_ButtonHasDisabledAttribute()
    {
        _connectivityMonitor.SetBluetooth(ConnectionIndicatorState.Connected);
        var cut = Render<ConnectViaWifiButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.Disabled, true));

        Assert.True(cut.Find("[data-testid='connect-via-wifi-button']").HasAttribute("disabled"));
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
}
