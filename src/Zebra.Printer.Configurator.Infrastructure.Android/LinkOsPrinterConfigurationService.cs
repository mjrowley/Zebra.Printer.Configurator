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
public sealed class LinkOsPrinterConfigurationService(IBluetoothPermissionService bluetoothPermissionService, IAppLog appLog)
    : IPrinterConfigurationService, IPrinterRestartService
{
    // Logged as-is; every other SGD value is safe to show, but the WiFi password should never
    // appear on screen (or in anything the user might screenshot/share for support).
    private static readonly HashSet<string> SensitiveKeys = ["wlan.wpa.psk"];

    public async Task ApplyAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await EnsureBluetoothPermissionAsync(cancellationToken);

        appLog.Log("Connecting to printer to apply WiFi configuration...");
        await BluetoothConnectionRunner.RunAsync(device.BluetoothMacAddress, connection =>
        {
            foreach (var (key, value) in WlanConfigurationCommandBuilder.BuildSetCommands(configuration))
            {
                var loggedValue = SensitiveKeys.Contains(key) ? "********" : value;
                appLog.Log($"Setting {key} = {loggedValue}");
                SGD.SET(key, value, connection);
            }
        }, appLog, cancellationToken);
        appLog.Log("WiFi configuration applied.", LogLevel.Success);
    }

    public async Task RestartAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureBluetoothPermissionAsync(cancellationToken);

        appLog.Log("Restarting printer...");
        await BluetoothConnectionRunner.RunAsync(device.BluetoothMacAddress, connection =>
        {
            // "device.restart" is not a real SGD command - SGD silently no-ops unrecognized
            // command names rather than erroring, which is why this previously appeared to
            // "succeed" without ever actually rebooting the printer. The documented command for a
            // soft reset is "device.reset" (confirmed against a real-world SGD trace for a
            // ZD-series printer: `! U1 do "device.reset" ""`).
            SGD.DO("device.reset", string.Empty, connection);
        }, appLog, cancellationToken);
        appLog.Log("Restart command sent.", LogLevel.Success);
    }

    private async Task EnsureBluetoothPermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
