using System.Text;

namespace Zebra.Printer.Configurator.Core.Validation;

public static class SsidValidator
{
    private const int MaxSsidBytes = 32;

    public static ValidationOutcome Validate(string? ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            return ValidationOutcome.Invalid("SSID is required.");
        }

        // WiFi SSIDs are limited to 32 bytes, not 32 characters - multi-byte UTF-8 characters count more.
        if (Encoding.UTF8.GetByteCount(ssid) > MaxSsidBytes)
        {
            return ValidationOutcome.Invalid($"SSID must be at most {MaxSsidBytes} bytes (UTF-8).");
        }

        return ValidationOutcome.Valid();
    }
}
