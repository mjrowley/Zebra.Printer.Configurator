using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Compares a newly-paired printer's actual Link-OS/firmware versions against the bundled baseline
/// for its model (see FirmwareBundleCatalog). Runs over Bluetooth - this happens right after
/// pairing, before the printer necessarily has any WiFi connection at all.
///
/// The printer's model comes from the documented "device.product_name" SGD key (its default value
/// format, "&lt;device.product_name&gt;-&lt;device.unique_id&gt;", is stated directly in Zebra's own
/// Link-OS 7.6.2 release notes). The exact SGD key for firmware version isn't confirmed from
/// documentation alone - "appl.name" (the conventional Zebra sample key) is tried first, falling
/// back to "device.firmware_version" if that comes back empty, mirroring the same
/// multi-candidate-marker resilience already used in NfcPrinterTagParser.BluetoothMacMarkers.
///
/// Link-OS version is read directly via the "appl.link_os_version_full" SGD key rather than the
/// SDK's ZebraPrinterLinkOs.LinkOsInformation wrapper - confirmed on-device that LinkOsInformation
/// reported a stale Link-OS version (7.6.0) after a firmware update that Zebra Setup Utilities
/// (reading the printer directly) confirmed had actually landed at 7.6.2. Deliberately "_full", not
/// plain "appl.link_os_version" - confirmed via Zebra Setup Utilities on-device that the plain key
/// only returns a two-part "major.minor" string (e.g. "7.6"). "_full" was originally assumed to
/// always return the complete three-part version, but a printer running 7.5.0 confirmed even "_full"
/// can report just "7.5" - see LinkOsVersion.TryParse, which accepts either form.
///
/// device.product_name is read with a short retry rather than accepted blank on the first try -
/// confirmed on-device that the automatic re-check right after a firmware update (triggered the
/// instant FirmwareUpdateForegroundService reports success, itself only once the SDK's own
/// UpdateFirmwareUnconditionally has confirmed the printer reconnected) can hit the printer in a
/// narrow window where its SGD command-processing subsystem hasn't finished initializing after the
/// reboot yet, even though the same query succeeds moments later when checked manually.
/// </summary>
public sealed class LinkOsPrinterVersionCheckService(IPrinterConnectionModeProvider connectionModeProvider, IAppLog appLog) : IPrinterVersionCheckService
{
    private const int MaxProductNameAttempts = 3;
    private static readonly TimeSpan ProductNameRetryDelay = TimeSpan.FromSeconds(2);

    public async Task<PrinterVersionCheckResult> CheckAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        appLog.Log("Checking printer firmware version...");

        // Firmware update/version check is out of scope for the header's Cancel button (a separate,
        // bigger piece of work - see PrinterOperationCancellation's doc comment), so no
        // active-connection tracking is passed here (cancellation: null). allowBleFallback: false -
        // this always runs shortly after a fresh Bluetooth bond (right after pairing, or after
        // PairAndConfigureWorkflow completes), and a BLE fallback attempted that soon can silently
        // trigger a second, unexpected OS pairing dialog (see PrinterConnectionRunner's own doc
        // comment); when this instead runs over WiFi (the Result.razor re-check), BLE fallback was
        // never reachable anyway, so disabling it here has no effect on that path.
        var result = await PrinterConnectionRunner.RunAsync(device, connectionModeProvider,
            connection => VersionCheckReader.Read(connection, () => GetProductNameWithRetry(connection), appLog),
            appLog, cancellation: null, cancellationToken, allowBleFallback: false);

        return result;
    }

    // Runs on a background thread already (this connection's whole delegate runs inside
    // PrinterConnectionRunner's own Task.Run), so a blocking Thread.Sleep between attempts is fine.
    private string? GetProductNameWithRetry(Connection connection)
    {
        var productName = SGD.GET("device.product_name", connection);
        for (var attempt = 2; attempt <= MaxProductNameAttempts && string.IsNullOrWhiteSpace(productName); attempt++)
        {
            appLog.Log($"Printer did not report a model name (attempt {attempt} of {MaxProductNameAttempts}) - it may still be finishing its post-firmware-update reboot. Retrying...", LogLevel.Warning);
            Thread.Sleep(ProductNameRetryDelay);
            productName = SGD.GET("device.product_name", connection);
        }

        return productName;
    }
}
