using System.Net;
using System.Net.Sockets;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Logging;

namespace Zebra.Printer.Configurator.IntegrationTests.Connectivity;

// Exercises the background polling loop against a real local TcpListener, same approach as
// TcpPortProbeTests - standing in for the printer's SGD port.
public class WifiConnectivityMonitorTests
{
    private static WifiConnectivityMonitor CreateMonitor(PrinterConnectivityMonitor connectivityMonitor, int port) =>
        new(connectivityMonitor, new AppLog())
        {
            Port = port,
            PollInterval = TimeSpan.FromMilliseconds(50),
            ProbeTimeout = TimeSpan.FromMilliseconds(500),
        };

    [Test]
    public async Task Start_SetsConnected_WhenSomethingIsListening()
    {
        using var listener = StartLoopbackListener(out var port);
        var connectivityMonitor = new PrinterConnectivityMonitor();
        var monitor = CreateMonitor(connectivityMonitor, port);

        try
        {
            monitor.Start("127.0.0.1");

            await WaitUntilAsync(() => connectivityMonitor.Wifi == ConnectionIndicatorState.Connected, TimeSpan.FromSeconds(3));

            Assert.That(connectivityMonitor.Wifi, Is.EqualTo(ConnectionIndicatorState.Connected));
        }
        finally
        {
            monitor.Stop();
        }
    }

    [Test]
    public async Task Start_SetsError_WhenNothingIsListening()
    {
        var port = GetFreeLoopbackPort();
        var connectivityMonitor = new PrinterConnectivityMonitor();
        var monitor = CreateMonitor(connectivityMonitor, port);

        try
        {
            monitor.Start("127.0.0.1");

            await WaitUntilAsync(() => connectivityMonitor.Wifi == ConnectionIndicatorState.Error, TimeSpan.FromSeconds(3));

            Assert.That(connectivityMonitor.Wifi, Is.EqualTo(ConnectionIndicatorState.Error));
        }
        finally
        {
            monitor.Stop();
        }
    }

    [Test]
    public async Task Stop_HaltsFurtherPolling()
    {
        using var listener = StartLoopbackListener(out var port);
        var connectivityMonitor = new PrinterConnectivityMonitor();
        var monitor = CreateMonitor(connectivityMonitor, port);
        monitor.Start("127.0.0.1");
        await WaitUntilAsync(() => connectivityMonitor.Wifi == ConnectionIndicatorState.Connected, TimeSpan.FromSeconds(3));

        monitor.Stop();
        listener.Stop();

        // If the loop kept running after Stop(), the now-closed listener would flip this to Error
        // on its next poll. Give it more than a few poll intervals' worth of time to prove it doesn't.
        await Task.Delay(400);
        Assert.That(connectivityMonitor.Wifi, Is.EqualTo(ConnectionIndicatorState.Connected));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition() && !cts.IsCancellationRequested)
        {
            await Task.Delay(20);
        }
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
