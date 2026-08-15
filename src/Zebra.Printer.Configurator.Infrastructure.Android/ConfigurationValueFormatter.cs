using Zebra.Printer.Configurator.Core.Abstractions;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Formats a raw SGD getvar result for display - shared between LinkOsPrinterConfigurationService's
/// on-demand "Check Configuration" read and LinkOsPrinterStatusReader's merged read, so there's one
/// copy of the redaction/formatting rules rather than two that could silently drift apart.
/// </summary>
internal static class ConfigurationValueFormatter
{
    // Redacts to a length rather than a fixed mask, so a mismatch between sent/read-back length is
    // still visible in the log without the actual WiFi password ever appearing on screen.
    private static readonly HashSet<string> SensitiveKeys = ["wlan.wpa.psk"];

    public static string Format(string key, string? value, PrinterConnectionMode mode)
    {
        if (SensitiveKeys.Contains(key))
        {
            return $"<redacted, length {value?.Length ?? 0}>";
        }

        // wlan.state only ever reports a real value when queried over the WiFi connection itself -
        // confirmed on-device: LinkOsConnectivityTestService reads a genuine value like "CONNECTED"
        // this same way after restart, but querying it over Bluetooth always comes back "?" (Zebra's
        // own SGD getvar convention for "no value to report") even once the printer is confirmed
        // connected to WiFi. Shown as a clearer explanation here rather than the bare "?".
        if (key == "wlan.state" && value == "?" && mode == PrinterConnectionMode.Bluetooth)
        {
            return "Not available over Bluetooth";
        }

        return value ?? "<null>";
    }
}
