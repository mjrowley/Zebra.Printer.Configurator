using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Printer;
// Zebra.Sdk.Printer itself declares its own PrinterStatus type, colliding with this app's - alias
// to disambiguate rather than renaming this app's own, more broadly-used type.
using PrinterStatus = Zebra.Printer.Configurator.Core.Models.PrinterStatus;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Reads firmware version, web interface state, and the full WLAN configuration list in one
/// connection (see PrinterStatus's own doc comment) - previously PrinterVersionAlert and
/// WebInterfaceTogglePanel each opened their own separate connection the instant they mounted, with
/// no sequencing between them; confirmed on-device this raced two concurrent Bluetooth Classic
/// connections to the same printer and corrupted one of the reads (a false "web interface disabled"
/// report on a printer that was actually enabled). One connection removes that race by construction.
///
/// No retry on a blank device.product_name, unlike LinkOsPrinterVersionCheckService.CheckAsync -
/// that retry exists specifically for the post-firmware-update-reboot race (the printer's SGD
/// subsystem not yet ready); this reader only ever runs right after pairing or on a manual recheck,
/// never in that context.
/// </summary>
public sealed class LinkOsPrinterStatusReader(
    IBluetoothPermissionService bluetoothPermissionService,
    IPrinterConnectionModeProvider connectionModeProvider,
    IAppLog appLog) : IPrinterStatusReader
{
    public async Task<PrinterStatus> ReadStatusAsync(PrinterDevice device, bool allowBleFallback = true, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log("Checking printer status (firmware version, web interface, configuration)...");
        var status = await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            var versionResult = VersionCheckReader.Read(connection, () => SGD.GET("device.product_name", connection), appLog);

            var webInterfaceState = new WebInterfaceState
            {
                HttpsEnabled = SGD.GET("ip.https.enable", connection) == "on",
                HttpEnabled = SGD.GET("ip.http.enable", connection) == "on",
            };
            appLog.Log($"Web interface is currently {(webInterfaceState.BothEnabled ? "enabled" : "disabled")}.");

            var configurationValues = new List<PrinterConfigurationValue>();
            foreach (var key in WlanDiagnosticKeys.All)
            {
                var value = SGD.GET(key, connection);
                configurationValues.Add(new PrinterConfigurationValue(key, ConfigurationValueFormatter.Format(key, value, connectionModeProvider.Mode)));
            }

            return new PrinterStatus
            {
                VersionResult = versionResult,
                WebInterfaceState = webInterfaceState,
                ConfigurationValues = configurationValues,
            };
        }, appLog, cancellation: null, cancellationToken, allowBleFallback);

        appLog.Log("Printer status check complete.", LogLevel.Success);
        return status;
    }
}
