using System.Net.Sockets;

namespace Zebra.Printer.Configurator.Core.Connectivity;

/// <summary>
/// Plain-socket TCP reachability check. Deliberately independent of the Zebra SDK's TcpConnection
/// (which only ships for net10.0-android/ios/windows, not plain net10.0) so this polling logic is
/// unit/integration-testable against a real local TcpListener.
/// </summary>
public static class TcpPortProbe
{
    public static async Task<bool> IsReachableAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
