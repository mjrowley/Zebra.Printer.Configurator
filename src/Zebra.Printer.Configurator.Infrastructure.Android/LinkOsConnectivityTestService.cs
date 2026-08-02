using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Configuration;
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
///
/// If the printer never appears on the network, reconnects via Bluetooth and reads back the actual
/// WLAN settings it's holding, logging each one - the most direct way to see which of the settings
/// LinkOsPrinterConfigurationService applied didn't actually stick, rather than continuing to guess
/// from the outside.
/// </summary>
public sealed class LinkOsConnectivityTestService(IAppLog appLog) : IPrinterConnectivityTestService
{
    private const int SgdPort = 6101;
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async Task<ConnectionTestResult> TestConnectionAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default)
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
            await LogPrinterWlanSettingsAsync(device, cancellationToken);
            return ConnectionTestResult.Failed($"{failure} Check the activity log for the printer's actual WLAN settings.");
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

    private async Task LogPrinterWlanSettingsAsync(PrinterDevice device, CancellationToken cancellationToken)
    {
        appLog.Log("Reconnecting via Bluetooth to check the printer's WLAN settings...");
        try
        {
            await BluetoothConnectionRunner.RunAsync(device.BluetoothMacAddress, connection =>
            {
                foreach (var key in WlanDiagnosticKeys.All)
                {
                    var value = SGD.GET(key, connection);
                    var displayValue = key == "wlan.wpa.psk"
                        ? $"<redacted, length {value?.Length ?? 0}>"
                        : value;
                    appLog.Log($"{key} = {displayValue}");
                }
            }, appLog, cancellationToken);
        }
        catch (Exception ex)
        {
            appLog.Log($"Could not reconnect via Bluetooth to check WLAN settings: {ex.Message}", LogLevel.Error);
        }
    }
}
