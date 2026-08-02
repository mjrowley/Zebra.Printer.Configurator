using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Confirms the printer rejoined the target WiFi network after restart: polls the new static IP
/// with plain sockets (TcpPortProbe/RetryPoller - the port a printer's still-rebooting network
/// stack won't answer on is exactly what the retry loop is for), then once reachable opens a real
/// Zebra SDK TcpConnection and reads wlan.state as positive confirmation.
/// </summary>
public sealed class LinkOsConnectivityTestService : IPrinterConnectivityTestService
{
    private const int SgdPort = 6101;
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async Task<ConnectionTestResult> TestConnectionAsync(WlanConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var reachable = await RetryPoller.PollUntilAsync(
            attempt: () => TcpPortProbe.IsReachableAsync(configuration.StaticIpAddress, SgdPort, ProbeTimeout, cancellationToken),
            timeout: PollTimeout,
            interval: PollInterval,
            cancellationToken: cancellationToken);

        if (!reachable)
        {
            return ConnectionTestResult.Failed(
                $"Printer did not respond on {configuration.StaticIpAddress}:{SgdPort} within {PollTimeout.TotalSeconds:N0}s after restart.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            Connection connection = new TcpConnection(configuration.StaticIpAddress, SgdPort);
            connection.Open();
            try
            {
                var wlanState = SGD.GET("wlan.state", connection);
                return string.IsNullOrWhiteSpace(wlanState)
                    ? ConnectionTestResult.Failed("Printer responded on the network but wlan.state was empty.")
                    : ConnectionTestResult.Succeeded(wlanState);
            }
            finally
            {
                connection.Close();
            }
        }, cancellationToken);
    }
}
