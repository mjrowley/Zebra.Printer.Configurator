namespace Zebra.Printer.Configurator.Core.Configuration;

public enum ConfigurationValueMatch
{
    /// <summary>No expected value for this key - it's read for display only, not something this app sets.</summary>
    Informational,

    /// <summary>The printer's reported value matches what this app expects it to be.</summary>
    Matches,

    /// <summary>The printer's reported value does not match what this app expects it to be.</summary>
    Mismatch,
}

/// <summary>
/// Decides whether a printer's reported value for an SGD key matches what this app expects it to be.
/// Extracted from LinkOsPrinterConfigurationService.ApplyAsync's own verification pass (which needs
/// the exact same logic to decide pass/fail for its Activity Log lines) so the Check Configuration
/// UI's colour-coding and the log verification can't silently drift into two different definitions
/// of "matches".
/// </summary>
public static class ConfigurationValueMatcher
{
    // Zebra's SGD docs state getvar on wlan.wpa.psk always prints a single "*" "for protection",
    // regardless of what was actually stored - so a match can't expect the sent value (a
    // 64-hexadecimal-digit PSK) to be echoed back; "*" itself IS the confirmation something was
    // accepted.
    private const string MaskedPskReadback = "*";

    // Confirmed on-device: reconfiguring a printer that was already connected to WiFi under a
    // different static IP/gateway reported wlan.ip.addr and wlan.ip.gateway as "mismatches"
    // immediately after SGD.SET, even though the new values applied correctly once the printer
    // restarted. Unlike wlan.ip.netmask, getvar on these two reflects the interface's current
    // *operational* value, not a newly-staged one, until the printer actually restarts. This only
    // matters to callers reading back a value they *just* SGD.SET within the same pre-restart
    // connection (LinkOsPrinterConfigurationService.ApplyAsync) - callers reading configuration after
    // a restart has already happened (the normal Check Configuration display, always shown after the
    // pairing workflow's restart+reconnect step) want a real match/mismatch here like any other key,
    // so this set is exposed for the pre-restart caller to skip evaluation on, not baked into
    // Evaluate itself.
    public static readonly IReadOnlyCollection<string> DeferredVerificationKeys = ["wlan.ip.addr", "wlan.ip.gateway"];

    public static ConfigurationValueMatch Evaluate(string key, string? expected, string? actual)
    {
        if (expected is null)
        {
            return ConfigurationValueMatch.Informational;
        }

        var matches = key == "wlan.wpa.psk"
            ? string.Equals(actual, MaskedPskReadback, StringComparison.Ordinal)
            : string.Equals(actual, expected, StringComparison.Ordinal);

        return matches ? ConfigurationValueMatch.Matches : ConfigurationValueMatch.Mismatch;
    }
}
