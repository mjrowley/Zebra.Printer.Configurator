using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Evaluates a printer's firmware version against its bundled baseline, given an already-open
/// connection - shared between LinkOsPrinterVersionCheckService.CheckAsync (which retries a blank
/// device.product_name read, for the specific post-firmware-reboot race its own doc comment
/// explains) and LinkOsPrinterStatusReader's merged read (which never runs in that context, so
/// passes a plain one-shot read instead) - getProductName is passed in as a delegate specifically
/// so that retry-or-not decision stays with each caller rather than being duplicated here.
/// </summary>
internal static class VersionCheckReader
{
    public static PrinterVersionCheckResult Read(Connection connection, Func<string?> getProductName, IAppLog appLog)
    {
        var productName = getProductName();
        var bundle = FirmwareBundleCatalog.FindByProductName(productName);
        if (bundle is null)
        {
            appLog.Log($"Printer model ('{productName}') is not a supported model for firmware version checks.", LogLevel.Warning);
            var unsupportedResult = PrinterVersionEvaluator.Evaluate(null, null, null);
            LogOutcome(unsupportedResult, appLog);
            return unsupportedResult;
        }

        var firmwareVersionFound = SGD.GET("appl.name", connection);
        if (string.IsNullOrWhiteSpace(firmwareVersionFound))
        {
            firmwareVersionFound = SGD.GET("device.firmware_version", connection);
        }

        var linkOsVersionFound = SGD.GET("appl.link_os_version_full", connection);

        var result = PrinterVersionEvaluator.Evaluate(bundle, linkOsVersionFound, firmwareVersionFound);
        LogOutcome(result, appLog);
        return result;
    }

    private static void LogOutcome(PrinterVersionCheckResult result, IAppLog appLog)
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
