namespace Zebra.Printer.Configurator.Core.Validation;

public static class WifiPasswordValidator
{
    private const int MinLength = 8;
    private const int MaxLength = 63;

    public static ValidationOutcome Validate(string? password)
    {
        // Empty is valid: it represents an open (unsecured) WiFi network.
        if (string.IsNullOrEmpty(password))
        {
            return ValidationOutcome.Valid();
        }

        if (password.Length < MinLength || password.Length > MaxLength)
        {
            return ValidationOutcome.Invalid(
                $"WiFi password must be between {MinLength} and {MaxLength} characters, or empty for an open network.");
        }

        return ValidationOutcome.Valid();
    }
}
