namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// Everything IPrinterStatusReader reads from a printer in one Bluetooth/WiFi connection -
/// firmware version, web interface state, and the full WLAN configuration list - so callers that
/// need more than one of these don't each open their own separate connection (which, for two
/// concurrent Bluetooth Classic connections to the same printer, corrupts one of the reads).
/// </summary>
public sealed record PrinterStatus
{
    public required PrinterVersionCheckResult VersionResult { get; init; }

    public required WebInterfaceState WebInterfaceState { get; init; }

    public required IReadOnlyList<PrinterConfigurationValue> ConfigurationValues { get; init; }
}
