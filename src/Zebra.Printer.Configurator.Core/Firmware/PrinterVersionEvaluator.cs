using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Firmware;

/// <summary>
/// Pure derivation of a PrinterVersionCheckResult from raw found version strings and the matched
/// bundle (or null, when the connected printer's model/firmware branch isn't recognized) - no SDK or
/// Android dependency, so the precedence rule below is independently unit-testable.
///
/// Precedence (per spec): if either Link-OS or firmware is higher than expected, that's reported
/// regardless of whether the other is simultaneously lower - "needs update" only applies when
/// nothing is higher and at least one is lower.
/// </summary>
public static class PrinterVersionEvaluator
{
    public static PrinterVersionCheckResult Evaluate(FirmwareBundle? bundle, string? linkOsVersionFound, string? firmwareVersionFound)
    {
        if (bundle is null)
        {
            return Unsupported(linkOsVersionFound, firmwareVersionFound);
        }

        if (!LinkOsVersion.TryParse(linkOsVersionFound, out var linkOsFound) ||
            !FirmwareVersion.TryParse(firmwareVersionFound, out var firmwareFound) ||
            !FirmwareVersion.TryParse(bundle.ExpectedFirmwareVersion, out var expectedFirmware))
        {
            return Unsupported(linkOsVersionFound, firmwareVersionFound);
        }

        var firmwareComparison = FirmwareVersionComparer.Compare(firmwareFound, expectedFirmware);
        if (firmwareComparison == FirmwareVersionComparison.Incomparable)
        {
            return Unsupported(linkOsVersionFound, firmwareVersionFound);
        }

        var linkOsComparison = linkOsFound.CompareTo(bundle.ExpectedLinkOsVersion);
        var anyHigher = linkOsComparison > 0 || firmwareComparison == FirmwareVersionComparison.Newer;
        var anyLower = linkOsComparison < 0 || firmwareComparison == FirmwareVersionComparison.Older;

        var outcome = anyHigher
            ? PrinterVersionOutcome.NewerThanExpected
            : anyLower
                ? PrinterVersionOutcome.NeedsUpdate
                : PrinterVersionOutcome.UpToDate;

        return new PrinterVersionCheckResult
        {
            Outcome = outcome,
            LinkOsVersionFound = linkOsVersionFound,
            FirmwareVersionFound = firmwareVersionFound,
            Bundle = bundle,
        };
    }

    private static PrinterVersionCheckResult Unsupported(string? linkOsVersionFound, string? firmwareVersionFound) =>
        new()
        {
            Outcome = PrinterVersionOutcome.Unsupported,
            LinkOsVersionFound = linkOsVersionFound,
            FirmwareVersionFound = firmwareVersionFound,
        };
}
