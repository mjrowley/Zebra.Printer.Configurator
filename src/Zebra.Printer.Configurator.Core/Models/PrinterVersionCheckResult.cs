using Zebra.Printer.Configurator.Core.Firmware;

namespace Zebra.Printer.Configurator.Core.Models;

public enum PrinterVersionOutcome
{
    UpToDate,
    NewerThanExpected,
    NeedsUpdate,
    Unsupported,
}

/// <summary>
/// Result of comparing a connected printer's actual Link-OS/firmware versions against the bundled
/// baseline for its model (see <see cref="FirmwareBundleCatalog"/>).
/// </summary>
public sealed record PrinterVersionCheckResult
{
    public required PrinterVersionOutcome Outcome { get; init; }

    public string? LinkOsVersionFound { get; init; }

    public string? FirmwareVersionFound { get; init; }

    /// <summary>The matched baseline this was compared against - null when Outcome is Unsupported.</summary>
    public FirmwareBundle? Bundle { get; init; }
}
