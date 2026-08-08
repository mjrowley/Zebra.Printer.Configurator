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
/// </summary>
public sealed class LinkOsPrinterVersionCheckService(IPrinterConnectionModeProvider connectionModeProvider, IAppLog appLog) : IPrinterVersionCheckService
{
    public async Task<PrinterVersionCheckResult> CheckAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        appLog.Log("Checking printer firmware version...");

        var result = await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            var productName = SGD.GET("device.product_name", connection);
            var bundle = FirmwareBundleCatalog.FindByProductName(productName);
            if (bundle is null)
            {
                appLog.Log($"Printer model ('{productName}') is not a supported model for firmware version checks.", LogLevel.Warning);
                return PrinterVersionEvaluator.Evaluate(null, null, null);
            }

            var firmwareVersionFound = SGD.GET("appl.name", connection);
            if (string.IsNullOrWhiteSpace(firmwareVersionFound))
            {
                firmwareVersionFound = SGD.GET("device.firmware_version", connection);
            }

            var linkOsInformation = ZebraPrinterFactory.GetLinkOsPrinter(connection).LinkOsInformation;
            var linkOsVersionFound = $"{linkOsInformation.Major}.{linkOsInformation.Minor}.{linkOsInformation.Micro}";

            return PrinterVersionEvaluator.Evaluate(bundle, linkOsVersionFound, firmwareVersionFound);
        }, appLog, cancellationToken);

        LogOutcome(result);
        return result;
    }

    private void LogOutcome(PrinterVersionCheckResult result)
    {
        switch (result.Outcome)
        {
            case PrinterVersionOutcome.UpToDate:
                appLog.Log("Printer firmware is up to date.", LogLevel.Success);
                break;
            case PrinterVersionOutcome.NewerThanExpected:
                appLog.Log($"Printer has a newer firmware than expected (Link-OS {result.LinkOsVersionFound}, firmware {result.FirmwareVersionFound}).", LogLevel.Warning);
                break;
            case PrinterVersionOutcome.NeedsUpdate:
                appLog.Log($"Printer requires a firmware update (Link-OS {result.LinkOsVersionFound}, firmware {result.FirmwareVersionFound}).", LogLevel.Warning);
                break;
            case PrinterVersionOutcome.Unsupported:
                appLog.Log("Printer firmware version could not be checked (unsupported model).", LogLevel.Warning);
                break;
        }
    }
}
