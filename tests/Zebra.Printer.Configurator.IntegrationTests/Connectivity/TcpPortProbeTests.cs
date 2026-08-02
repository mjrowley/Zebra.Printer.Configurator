using System.Net;
using System.Net.Sockets;
using Zebra.Printer.Configurator.Core.Connectivity;

namespace Zebra.Printer.Configurator.IntegrationTests.Connectivity;

// Exercises the TCP polling logic our own code owns against a real local TcpListener, standing in
// for the printer's SGD port. This is the CI-runnable subset described in the plan - no Zebra SDK
// or Android runtime involved, just plain sockets.
public class TcpPortProbeTests
{
    [Test]
    public async Task IsReachableAsync_ReturnsTrue_WhenSomethingIsListening()
    {
        using var listener = StartLoopbackListener(out var port);

        var reachable = await TcpPortProbe.IsReachableAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));

        Assert.That(reachable, Is.True);
    }

    [Test]
    public async Task IsReachableAsync_ReturnsFalse_WhenNothingIsListening()
    {
        var port = GetFreeLoopbackPort();

        var reachable = await TcpPortProbe.IsReachableAsync("127.0.0.1", port, TimeSpan.FromMilliseconds(500));

        Assert.That(reachable, Is.False);
    }

    [Test]
    public async Task PollUntilAsync_DetectsPrinterComingOnlineMidPoll()
    {
        var port = GetFreeLoopbackPort();
        TcpListener? listener = null;

        var pollTask = RetryPoller.PollUntilAsync(
            attempt: () => TcpPortProbe.IsReachableAsync("127.0.0.1", port, TimeSpan.FromMilliseconds(500)),
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(200));

        // Simulate the printer finishing its reboot partway through the polling window.
        await Task.Delay(400);
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        try
        {
            var result = await pollTask;

            Assert.That(result, Is.True);
        }
        finally
        {
            listener.Stop();
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
