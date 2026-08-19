using Zebra.Printer.Configurator.Core.Abstractions;

namespace Zebra.Printer.Configurator.Core.Connectivity;

/// <summary>
/// Concrete IWifiConnectivityMonitor: polls the printer's SGD port with plain sockets
/// (TcpPortProbe - same primitive LinkOsConnectivityTestService uses for the initial post-restart
/// check) on a fixed interval, updating PrinterConnectivityMonitor.Wifi after every attempt. Runs
/// as a long-lived background loop independent of any particular page being mounted, so the
/// indicator keeps reflecting live reachability even after navigating away from Progress/Result.
/// </summary>
public sealed class WifiConnectivityMonitor(PrinterConnectivityMonitor connectivityMonitor, IAppLog appLog) : IWifiConnectivityMonitor
{
    // General SGD/status traffic - see PrinterConnectionRunner's own doc comment (Infrastructure.Android)
    // for why this differs from the file-transfer-only port (6101), confirmed via direct on-device
    // port testing against the printer (2026-08-19).
    private const int DefaultSgdPort = 9100;

    // Mutable (not const/readonly) so integration tests can point this at a local TcpListener on an
    // arbitrary free port, and shrink the interval/timeout for fast, deterministic polling instead
    // of waiting out the real production values.
    public int Port { get; set; } = DefaultSgdPort;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(3);

    private CancellationTokenSource? _cts;

    public void Start(string ipAddress)
    {
        Stop();

        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = RunAsync(ipAddress, cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task RunAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var reachable = await TcpPortProbe.IsReachableAsync(ipAddress, Port, ProbeTimeout, cancellationToken).ConfigureAwait(false);
                connectivityMonitor.SetWifi(reachable ? ConnectionIndicatorState.Connected : ConnectionIndicatorState.Error);

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped intentionally (Stop() called, or a new pairing attempt reset the session) -
            // not an error, so nothing further to log or report.
        }
        catch (Exception ex)
        {
            appLog.Log($"WiFi connectivity monitoring stopped unexpectedly: {ex.Message}", LogLevel.Warning);
        }
    }
}
