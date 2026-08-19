using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
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
///
/// WlanConfiguration.IpAddressMode == Dhcp has no known IP to poll this way - TestDhcpConnectionAsync
/// instead reconnects over Bluetooth after a fixed settling delay and reads wlan.ip.addr back to
/// discover what the printer was actually assigned, then confirms it with the same TCP reachability
/// check as the Static path before reporting success.
/// </summary>
public sealed class LinkOsConnectivityTestService(IAppLog appLog, PrinterConnectivityMonitor connectivityMonitor, PrinterOperationCancellation cancellation) : IPrinterConnectivityTestService
{
    // Confirmed via direct on-device port testing against the printer (2026-08-19): general SGD
    // traffic (this reachability probe, plus the wlan.state read right after) only responds
    // reliably on 9100 - 6101 is reserved for actual file transfers (see PrinterConnectionRunner's
    // own FileTransferSgdPort). Using 6101 here was the root cause of a "Printer did not respond"
    // timeout being reported even when the printer had genuinely rejoined WiFi.
    private const int SgdPort = 9100;
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    // Confirmed on-device: a printer can read back wlan.ip.addr over Bluetooth - matching the target
    // Static IP exactly - just seconds after the Static path's own PollTimeout above already gave up,
    // i.e. it did rejoin WiFi correctly, just slightly slower than PollTimeout allows for (SGD service
    // startup lagging a beat behind IP association on some WiFi networks). Rather than reporting a
    // false failure for a printer that's actually fine, LogPrinterWlanSettingsAsync's already-collected
    // readback (run regardless, for diagnostics) is checked for exactly that positive signal, and given
    // one short extra window to answer before giving up for real.
    private static readonly TimeSpan LateRejoinRetryTimeout = TimeSpan.FromSeconds(20);

    // DHCP mode has no known IP to poll over TCP the way Static mode does below - the printer's new
    // address can only be learned by reconnecting over Bluetooth and reading wlan.ip.addr back.
    // BluetoothConnectionRunner.RunAsync already retries internally (3 attempts/~9s) and a
    // device.reset drops Bluetooth immediately, so a fixed settling delay up front (mirrors
    // WebInterfaceTogglePanel.RestartSettlingDelay's same reasoning) avoids burning through poll
    // attempts during the guaranteed-dead window while the printer is still rebooting.
    private static readonly TimeSpan DhcpSettlingDelay = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DhcpPollTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DhcpPollInterval = TimeSpan.FromSeconds(3);

    public Task<ConnectionTestResult> TestConnectionAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default)
    {
        connectivityMonitor.SetWifi(ConnectionIndicatorState.Connecting);

        return configuration.IpAddressMode == WlanIpAddressMode.Dhcp
            ? TestDhcpConnectionAsync(device, cancellationToken)
            : TestStaticConnectionAsync(device, configuration, cancellationToken);
    }

    private async Task<ConnectionTestResult> TestStaticConnectionAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken)
    {
        appLog.Log($"Waiting for printer to rejoin WiFi at {configuration.StaticIpAddress} (up to {PollTimeout.TotalSeconds:N0}s)...");

        var reachable = await RetryPoller.PollUntilAsync(
            attempt: () => TcpPortProbe.IsReachableAsync(configuration.StaticIpAddress, SgdPort, ProbeTimeout, cancellationToken),
            timeout: PollTimeout,
            interval: PollInterval,
            cancellationToken: cancellationToken);

        // PollUntilAsync can't tell an external cancellation apart from its own timeout internally
        // (both just make it return false) - checked explicitly here so a cancelled poll is reported
        // as a cancellation, not treated as "the printer never came back" and sent into the
        // Bluetooth-reconnect failure diagnostics below.
        cancellationToken.ThrowIfCancellationRequested();

        if (!reachable)
        {
            var confirmedIp = await LogPrinterWlanSettingsAsync(device, cancellationToken);
            if (string.Equals(confirmedIp, configuration.StaticIpAddress, StringComparison.OrdinalIgnoreCase))
            {
                appLog.Log($"Printer's own WLAN settings confirm it rejoined at {configuration.StaticIpAddress} - retrying briefly before giving up...");
                reachable = await RetryPoller.PollUntilAsync(
                    attempt: () => TcpPortProbe.IsReachableAsync(configuration.StaticIpAddress, SgdPort, ProbeTimeout, cancellationToken),
                    timeout: LateRejoinRetryTimeout,
                    interval: PollInterval,
                    cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!reachable)
            {
                var failure = $"Printer did not respond on {configuration.StaticIpAddress}:{SgdPort} within {PollTimeout.TotalSeconds:N0}s after restart.";
                appLog.Log(failure, LogLevel.Error);
                connectivityMonitor.SetWifi(ConnectionIndicatorState.Error);
                return ConnectionTestResult.Failed($"{failure} Check the activity log for the printer's actual WLAN settings.", configuration.StaticIpAddress);
            }
        }

        appLog.Log($"Printer is reachable at {configuration.StaticIpAddress}:{SgdPort}. Confirming WiFi state...");
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            Connection connection = new TcpConnection(configuration.StaticIpAddress, SgdPort);
            connection.Open();
            using var _ = cancellation.TrackActiveConnection(connection.Close);
            try
            {
                var wlanState = SGD.GET("wlan.state", connection);
                if (string.IsNullOrWhiteSpace(wlanState))
                {
                    appLog.Log("Printer responded on the network but wlan.state was empty.", LogLevel.Error);
                    connectivityMonitor.SetWifi(ConnectionIndicatorState.Error);
                    return ConnectionTestResult.Failed("Printer responded on the network but wlan.state was empty.", configuration.StaticIpAddress);
                }

                appLog.Log($"WiFi state: {wlanState}", LogLevel.Success);
                connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
                return ConnectionTestResult.Succeeded(wlanState, configuration.StaticIpAddress);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            finally
            {
                connection.Close();
            }
        }, cancellationToken);
    }

    private async Task<ConnectionTestResult> TestDhcpConnectionAsync(PrinterDevice device, CancellationToken cancellationToken)
    {
        appLog.Log($"Waiting {DhcpSettlingDelay.TotalSeconds:N0}s for the printer to reboot before reconnecting to read its DHCP-assigned address...");
        await Task.Delay(DhcpSettlingDelay, cancellationToken);

        string? assignedIp = null;
        string? wlanState = null;

        var confirmed = await RetryPoller.PollUntilAsync(
            attempt: async () =>
            {
                try
                {
                    await BluetoothConnectionRunner.RunAsync(device.BluetoothMacAddress, connection =>
                    {
                        assignedIp = SGD.GET("wlan.ip.addr", connection);
                        wlanState = SGD.GET("wlan.state", connection);
                    }, appLog, cancellation, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Printer still rebooting / Bluetooth not back up yet - not a failure, the poll
                    // loop below just tries again until DhcpPollTimeout elapses.
                    return false;
                }

                if (string.IsNullOrWhiteSpace(assignedIp) || assignedIp == "0.0.0.0")
                {
                    // wlan.ip.addr reads back blank/unset for a moment even after Bluetooth is back
                    // up, until the DHCP lease actually completes.
                    return false;
                }

                // Confirms the discovered address is actually reachable, matching the same
                // confidence level as the Static path's own TCP probe above, rather than trusting
                // the SGD readback alone.
                return await TcpPortProbe.IsReachableAsync(assignedIp, SgdPort, ProbeTimeout, cancellationToken);
            },
            timeout: DhcpPollTimeout,
            interval: DhcpPollInterval,
            cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!confirmed || string.IsNullOrWhiteSpace(assignedIp))
        {
            var failure = $"Printer did not report a reachable DHCP-assigned IP address within {DhcpSettlingDelay.TotalSeconds + DhcpPollTimeout.TotalSeconds:N0}s after restart.";
            appLog.Log(failure, LogLevel.Error);
            connectivityMonitor.SetWifi(ConnectionIndicatorState.Error);
            await LogPrinterWlanSettingsAsync(device, cancellationToken);
            return ConnectionTestResult.Failed($"{failure} Check the activity log for the printer's actual WLAN settings.");
        }

        appLog.Log($"Printer is reachable via DHCP at {assignedIp}:{SgdPort}. WiFi state: {wlanState}", LogLevel.Success);
        connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        return ConnectionTestResult.Succeeded(wlanState ?? "unknown", assignedIp);
    }

    // Returns the wlan.ip.addr value observed during this readback (null if the reconnect itself
    // failed, or blank/"0.0.0.0" if the printer genuinely has no WiFi association) - callers use this
    // as positive confirmation of a late-but-real rejoin (see LateRejoinRetryTimeout above), on top of
    // the unconditional logging every key gets regardless.
    private async Task<string?> LogPrinterWlanSettingsAsync(PrinterDevice device, CancellationToken cancellationToken)
    {
        appLog.Log("Reconnecting via Bluetooth to check the printer's WLAN settings...");
        string? ipAddress = null;
        try
        {
            await BluetoothConnectionRunner.RunAsync(device.BluetoothMacAddress, connection =>
            {
                foreach (var key in WlanDiagnosticKeys.All)
                {
                    var value = SGD.GET(key, connection);
                    if (key == "wlan.ip.addr")
                    {
                        ipAddress = value;
                    }

                    // wlan.state only ever reports a real value when queried over the WiFi
                    // connection itself - this fallback path always runs over Bluetooth, so it
                    // always comes back "?" (Zebra's own SGD getvar convention for "no value to
                    // report") regardless of whether the printer is actually connected to WiFi.
                    var displayValue = key switch
                    {
                        "wlan.wpa.psk" => $"<redacted, length {value?.Length ?? 0}>",
                        "wlan.state" when value == "?" => "Not available over Bluetooth",
                        _ => value,
                    };
                    appLog.Log($"{key} = {displayValue}");
                }
            }, appLog, cancellation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Best-effort diagnostic logging only swallows genuine failures below - a cancellation
            // must still propagate so TestConnectionAsync (and PairAndConfigureWorkflow above it)
            // see it as a cancel, not report this as a normal connectivity-test failure.
            throw;
        }
        catch (Exception ex)
        {
            appLog.Log($"Could not reconnect via Bluetooth to check WLAN settings: {ex.Message}", LogLevel.Error);
        }

        return ipAddress;
    }
}
