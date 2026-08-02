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
public sealed class LinkOsConnectivityTestService(IAppLog appLog) : IPrinterConnectivityTestService
{
    private const int SgdPort = 6101;
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async Task<ConnectionTestResult> TestConnectionAsync(WlanConfiguration configuration, CancellationToken cancellationToken = default)
    {
        appLog.Log($"Waiting for printer to rejoin WiFi at {configuration.StaticIpAddress} (up to {PollTimeout.TotalSeconds:N0}s)...");

        var reachable = await RetryPoller.PollUntilAsync(
            attempt: () => TcpPortProbe.IsReachableAsync(configuration.StaticIpAddress, SgdPort, ProbeTimeout, cancellationToken),
            timeout: PollTimeout,
            interval: PollInterval,
            cancellationToken: cancellationToken);

        if (!reachable)
        {
            var failure = $"Printer did not respond on {configuration.StaticIpAddress}:{SgdPort} within {PollTimeout.TotalSeconds:N0}s after restart.";
            appLog.Log(failure, LogLevel.Error);
            return ConnectionTestResult.Failed(failure);
        }

        appLog.Log($"Printer is reachable at {configuration.StaticIpAddress}:{SgdPort}. Confirming WiFi state...");
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            Connection connection = new TcpConnection(configuration.StaticIpAddress, SgdPort);
            connection.Open();
            try
            {
                var wlanState = SGD.GET("wlan.state", connection);
                if (string.IsNullOrWhiteSpace(wlanState))
                {
                    appLog.Log("Printer responded on the network but wlan.state was empty.", LogLevel.Error);
                    return ConnectionTestResult.Failed("Printer responded on the network but wlan.state was empty.");
                }

                appLog.Log($"WiFi state: {wlanState}", LogLevel.Success);
                return ConnectionTestResult.Succeeded(wlanState);
            }
            finally
            {
                connection.Close();
            }
        }, cancellationToken);
    }
}
