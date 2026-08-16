using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
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
    PairingSession session,
    IAppLog appLog) : IPrinterStatusReader
{
    public async Task<PrinterStatus> ReadStatusAsync(PrinterDevice device, bool allowBleFallback = true, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        // The fixed label-printing defaults/apl.enable are known targets regardless of whether this
        // pairing attempt has reached Configure yet - in particular, this reader also runs from the
        // Pairing page itself for an already-configured printer being re-tapped (no
        // Session.Configuration this session at all), which is exactly the case this colour-coding
        // is meant to help with. Session.Configuration - only set once the user submits the Configure
        // form - additionally supplies device.friendly_name and the WLAN keys once it exists.
        var expected = session.Configuration is { } configuration
            ? PrinterDefaultsCommandBuilder.BuildExpectedDiagnosticValues(configuration.PrinterName, configuration)
            : PrinterDefaultsCommandBuilder.BuildFixedDiagnosticDefaults();

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
                var match = ConfigurationValueMatcher.Evaluate(key, expected.GetValueOrDefault(key), value);
                configurationValues.Add(new PrinterConfigurationValue(key, ConfigurationValueFormatter.Format(key, value, connectionModeProvider.Mode), match));
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
