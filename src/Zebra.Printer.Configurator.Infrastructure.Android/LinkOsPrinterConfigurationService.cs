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
    // Logged as-is; every other SGD value is safe to show, but the WiFi password should never
    // appear on screen (or in anything the user might screenshot/share for support).
    private static readonly HashSet<string> SensitiveKeys = ["wlan.wpa.psk"];

    // Zebra's SGD docs state getvar on wlan.wpa.psk always prints a single "*" "for protection",
    // regardless of what was actually stored - so a verification pass can't expect the sent value
    // (a 64-hex-digit PSK) to be echoed back; "*" itself IS the confirmation something was accepted.
    private const string MaskedPskReadback = "*";

    // Confirmed on-device: reconfiguring a printer that was already connected to WiFi under a
    // different static IP/gateway reported wlan.ip.addr and wlan.ip.gateway as "mismatches" even
    // though the new values applied correctly after restart. Unlike wlan.ip.netmask, getvar on
    // these two reflects the interface's current *operational* value, not the newly staged one - it
    // can only be meaningfully verified after the restart+reconnect LinkOsConnectivityTestService
    // performs, not immediately after SGD.SET within this same connection.
    private static readonly HashSet<string> DeferredVerificationKeys = ["wlan.ip.addr", "wlan.ip.gateway"];

    // "device.reset" makes the printer reboot, so it commonly never sends a response at all - the
    // default SGD.DO read timeout is tuned for commands that always answer, so waiting for it here
    // just stalls every reset for no reason. This short timeout still gives the printer a fair
    // chance to ack (Bluetooth Classic round-trips are well under a second locally) but returns as
    // soon as that ack arrives, or gives up quickly if none ever comes, rather than blocking the
    // full default duration either way.
    private const int ResetReadTimeoutMs = 3000;
    private const int ResetTimeToWaitForMoreDataMs = 500;

    public async Task ApplyAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await EnsureConnectionPermissionAsync(cancellationToken);

        appLog.Log($"Connecting to printer over {connectionModeProvider.Mode} to apply WiFi configuration...");
        await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            var commands = WlanConfigurationCommandBuilder.BuildSetCommands(configuration);

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

                if (DeferredVerificationKeys.Contains(key))
                {
                    appLog.Log($"{key}: sent '{DisplayValue(key, value)}' (currently reports '{DisplayValue(key, actual)}' - takes effect after restart)");
                    continue;
                }

                var matches = key == "wlan.wpa.psk"
                    ? string.Equals(actual, MaskedPskReadback, StringComparison.Ordinal)
                    : string.Equals(actual, value, StringComparison.Ordinal);
                appLog.Log(
                    matches
                        ? $"{key}: confirmed ({DisplayValue(key, actual)})"
                        : $"{key}: MISMATCH - sent '{DisplayValue(key, value)}', printer has '{DisplayValue(key, actual)}'",
                    matches ? LogLevel.Success : LogLevel.Warning);
            }
        }, appLog, cancellationToken);
        appLog.Log("WiFi configuration applied.", LogLevel.Success);
    }

    public async Task RestartAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureConnectionPermissionAsync(cancellationToken);

        appLog.Log("Restarting printer...");
        await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            // "device.restart" is not a real SGD command - SGD silently no-ops unrecognized
            // command names rather than erroring, which is why this previously appeared to
            // "succeed" without ever actually rebooting the printer. The documented command for a
            // soft reset is "device.reset" (confirmed against a real-world SGD trace for a
            // ZD-series printer: `! U1 do "device.reset" ""`).
            SGD.DO("device.reset", string.Empty, connection, ResetReadTimeoutMs, ResetTimeToWaitForMoreDataMs);
        }, appLog, cancellationToken);
        appLog.Log("Restart command sent.", LogLevel.Success);
    }

    public async Task ResetToFactoryDefaultsAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureConnectionPermissionAsync(cancellationToken);

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
        }, appLog, cancellationToken);
        appLog.Log(
            "Factory reset command sent. The printer is restarting with default network and Bluetooth settings - Bluetooth pairing may need to be redone.",
            LogLevel.Warning);
    }

    public async Task<IReadOnlyList<PrinterConfigurationValue>> ReadConfigurationAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureConnectionPermissionAsync(cancellationToken);

        appLog.Log($"Connecting to printer over {connectionModeProvider.Mode} to check configuration...");
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
        }, appLog, cancellationToken);
        appLog.Log("Configuration check complete.", LogLevel.Success);

        return values;
    }

    // Redacts to a length rather than a fixed mask, so a mismatch between sent/read-back length is
    // still visible in the log without the actual WiFi password ever appearing on screen.
    private static string DisplayValue(string key, string? value) =>
        SensitiveKeys.Contains(key) ? $"<redacted, length {value?.Length ?? 0}>" : value ?? "<null>";

    private async Task EnsureConnectionPermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No Bluetooth permission needed at all when the active transport is WiFi - nothing here
        // touches the Bluetooth stack in that case.
        if (connectionModeProvider.Mode != PrinterConnectionMode.Bluetooth)
        {
            return;
        }

        // Requested/awaited here, on the calling context, rather than inside Task.Run below -
        // showing the system permission dialog needs the Activity, not a background thread-pool thread.
        var granted = await bluetoothPermissionService.EnsureGrantedAsync(cancellationToken);
        if (!granted)
        {
            appLog.Log("Bluetooth permission is required to configure the printer.", LogLevel.Error);
            throw new InvalidOperationException(
                "Bluetooth permission is required to configure the printer. Please grant it and try again.");
        }
    }
}
