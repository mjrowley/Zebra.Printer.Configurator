using System.Text;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Applies WLAN configuration and issues the restart command over Bluetooth - the printer isn't on
/// the target WiFi network yet at this point, so Bluetooth (paired via the MAC address read from
/// the NFC tag) is the only connection available.
///
/// Deliberately does NOT call BluetoothDevice.CreateBond() before connecting. An earlier version
/// of this class did, on the theory that an unbonded RFCOMM connection was the cause of
/// intermittent "read failed, socket might closed or timeout" errors - but on-device testing showed
/// that attempt itself triggering a PIN-based pairing negotiation that failed (visible as an
/// Android system "PIN error" toast) and left the connection in a worse state, with the same read
/// failure recurring afterward. Zebra SPP printers commonly accept RFCOMM connections without a
/// prior OS-level bond at all, so skipping bonding avoids provoking that failed negotiation.
/// Bonding still happens, but earlier and correctly, via BluetoothPairingService/IBluetoothPairingService.
/// </summary>
public sealed class LinkOsPrinterConfigurationService(
    IBluetoothPermissionService bluetoothPermissionService,
    IPrinterConnectionModeProvider connectionModeProvider,
    IAppLog appLog)
    : IPrinterConfigurationService, IPrinterRestartService, IPrinterFactoryResetService, IPrinterConfigurationReader
{
    // "device.reset" makes the printer reboot, so it commonly never sends a response at all - the
    // default SGD.DO read timeout is tuned for commands that always answer, so waiting for it here
    // just stalls every reset for no reason. This short timeout still gives the printer a fair
    // chance to ack (Bluetooth Classic round-trips are well under a second locally) but returns as
    // soon as that ack arrives, or gives up quickly if none ever comes, rather than blocking the
    // full default duration either way.
    private const int ResetReadTimeoutMs = 3000;
    private const int ResetTimeToWaitForMoreDataMs = 500;

    public async Task ApplyAsync(PrinterDevice device, WlanConfiguration configuration, IPrinterConnectionSession session, CancellationToken cancellationToken = default)
    {
        appLog.Log("Applying WiFi configuration...");
        // Cast is safe - PrinterConnectionSessionFactory is the only production implementation of
        // IPrinterConnectionSession; see PrinterConnectionSession's doc comment for why the public
        // Core interface itself can't expose RunAsync (Core can't reference Zebra.Sdk.Comm.Connection).
        await ((PrinterConnectionSession)session).RunAsync(connection =>
        {
            var commands = WlanConfigurationCommandBuilder.BuildSetCommands(configuration)
                .Concat(PrinterDefaultsCommandBuilder.BuildSetCommands(configuration.PrinterName))
                .ToList();

            foreach (var (key, value) in commands)
            {
                appLog.Log($"Setting {key} = {DisplayValue(key, value)}");
                SGD.SET(key, value, connection);
            }

            // Read every value straight back and compare against what was sent, rather than
            // trusting that SET succeeding means it stuck - SGD.SET on a key the printer doesn't
            // actually recognize succeeds silently and just stores it inertly under that name,
            // which is exactly how wlan.password/device.restart/wlan.ssid all looked "confirmed"
            // on readback while never doing anything.
            appLog.Log("Verifying settings were saved...");
            foreach (var (key, value) in commands)
            {
                var actual = SGD.GET(key, connection);

                if (ConfigurationValueMatcher.DeferredVerificationKeys.Contains(key))
                {
                    appLog.Log($"{key}: sent '{DisplayValue(key, value)}' (currently reports '{DisplayValue(key, actual)}' - takes effect after restart)");
                    continue;
                }

                var matches = ConfigurationValueMatcher.Evaluate(key, value, actual) == ConfigurationValueMatch.Matches;
                appLog.Log(
                    matches
                        ? $"{key}: confirmed ({DisplayValue(key, actual)})"
                        : $"{key}: MISMATCH - sent '{DisplayValue(key, value)}', printer has '{DisplayValue(key, actual)}'",
                    matches ? LogLevel.Success : LogLevel.Warning);
            }
        }, cancellationToken);
        appLog.Log("WiFi configuration applied.", LogLevel.Success);
    }

    public async Task RestartAsync(PrinterDevice device, IPrinterConnectionSession session, CancellationToken cancellationToken = default)
    {
        appLog.Log("Restarting printer...");
        await ((PrinterConnectionSession)session).RunAsync(connection =>
        {
            // "device.restart" is not a real SGD command - SGD silently no-ops unrecognized
            // command names rather than erroring, which is why this previously appeared to
            // "succeed" without ever actually rebooting the printer. The documented command for a
            // soft reset is "device.reset" (confirmed against a real-world SGD trace for a
            // ZD-series printer: `! U1 do "device.reset" ""`).
            SGD.DO("device.reset", string.Empty, connection, ResetReadTimeoutMs, ResetTimeToWaitForMoreDataMs);
        }, cancellationToken);
        appLog.Log("Restart command sent.", LogLevel.Success);
    }

    public async Task ResetToFactoryDefaultsAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log("Sending factory reset command to printer...", LogLevel.Warning);
        await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            // "^JUF" ("reload factory settings") is the ZPL Configuration Update command - Zebra's
            // own Programming Guide gives this exact sequence as the way to factory-reset a printer:
            // "To return the printer to the default factory settings using ZPL, send this:
            // ^XA ^JUF ^XZ". It's raw ZPL, not an SGD command, so it's written directly to the
            // connection rather than going through SGD.SET/DO.
            connection.Write(Encoding.ASCII.GetBytes("^XA^JUF^XZ"));

            // Confirmed on-device: ^JUF alone leaves WLAN and Bluetooth pairing state untouched -
            // consistent with the Programming Guide's own notes throughout the wireless SGD
            // reference that certain settings are unaffected by "^JUF ... and device.restore_defaults".
            // device.restore_defaults resets one SGD branch wholesale; "wlan" is one of its three
            // documented branches (the others being "ip" and "internal_wired").
            appLog.Log("Restoring network settings to factory defaults...");
            SGD.DO("device.restore_defaults", "wlan", connection);

            // Clears the printer's own memory of previously-paired devices ("Deletes all information
            // related to previous Bluetooth pairing events from the printer", per the SGD Network
            // Commands reference) - the host side of the same stale pairing state is cleared
            // separately by IBluetoothPairingService.RemoveBondAsync after this call returns.
            appLog.Log("Clearing Bluetooth pairing cache...");
            SGD.DO("bluetooth.clear_bonding_cache", string.Empty, connection);

            // ^JUF alone only reloads the factory defaults into the printer's active configuration -
            // per the same Programming Guide, that reload "is lost at power-off if not saved". A
            // reset makes it actually take effect and persist rather than silently reverting on the
            // next power cycle - the same soft-reset command RestartAsync uses, sent here before the
            // connection closes rather than left for the caller to trigger separately.
            appLog.Log("Restarting printer to apply factory defaults...");
            SGD.DO("device.reset", string.Empty, connection, ResetReadTimeoutMs, ResetTimeToWaitForMoreDataMs);
            // Factory reset never runs while the header's Cancel button is visible (it's a separate,
            // mutually-exclusive Pairing-page flow), so no active-connection tracking is needed here.
        }, appLog, cancellation: null, cancellationToken);
        appLog.Log(
            "Factory reset command sent. The printer is restarting with default network and Bluetooth settings - Bluetooth pairing may need to be redone.",
            LogLevel.Warning);
    }

    public async Task<IReadOnlyList<PrinterConfigurationValue>> ReadConfigurationAsync(PrinterDevice device, bool allowBleFallback = true, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log($"Connecting to printer over {connectionModeProvider.Mode} to check configuration...");
        // Check Configuration never runs while the header's Cancel button is visible (it's a
        // separate, mutually-exclusive Pairing-page flow), so no active-connection tracking is
        // needed here - cancellation: null.
        var values = await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            var results = new List<PrinterConfigurationValue>();
            foreach (var key in WlanDiagnosticKeys.All)
            {
                var value = SGD.GET(key, connection);
                // Values are display-safe (redacted) here, at the source, rather than left to
                // whatever renders them - the retrieved values are shown directly in the UI, not
                // routed through the Activity Log's own DisplayValue-at-log-time convention.
                results.Add(new PrinterConfigurationValue(key, DisplayValue(key, value)));
            }

            return (IReadOnlyList<PrinterConfigurationValue>)results;
        }, appLog, cancellation: null, cancellationToken, allowBleFallback);
        appLog.Log("Configuration check complete.", LogLevel.Success);

        return values;
    }

    // Delegates to the shared ConfigurationValueFormatter (also used by LinkOsPrinterStatusReader's
    // merged read) rather than keeping its own copy of the redaction/formatting rules.
    private string DisplayValue(string key, string? value) =>
        ConfigurationValueFormatter.Format(key, value, connectionModeProvider.Mode);
}
