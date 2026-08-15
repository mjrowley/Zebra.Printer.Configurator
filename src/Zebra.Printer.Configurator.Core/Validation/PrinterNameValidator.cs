namespace Zebra.Printer.Configurator.Core.Validation;

public static class PrinterNameValidator
{
    public static ValidationOutcome Validate(string? printerName)
    {
        // No confirmed max length for device.friendly_name in Zebra's own SGD documentation
        // (unlike SsidValidator's documented 32-byte WiFi SSID limit) - only reject blank rather
        // than invent an unverified cap.
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return ValidationOutcome.Invalid("Printer Name is required.");
        }

        return ValidationOutcome.Valid();
    }
}
