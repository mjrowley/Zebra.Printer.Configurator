namespace Zebra.Printer.Configurator.Core.Validation;

public static class IPv4Validator
{
    private const string InvalidFormatMessage = "IP address must be in dotted-quad IPv4 format (e.g. 192.168.1.100).";

    public static ValidationOutcome Validate(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return ValidationOutcome.Invalid("IP address is required.");
        }

        var segments = ipAddress.Split('.');
        if (segments.Length != 4)
        {
            return ValidationOutcome.Invalid(InvalidFormatMessage);
        }

        foreach (var segment in segments)
        {
            // Reject leading zeros (e.g. "010") - some tools interpret them as octal, a classic
            // source of parsing-inconsistency bugs between this app and the printer/network stack.
            var isLeadingZero = segment.Length > 1 && segment[0] == '0';
            if (segment.Length is 0 or > 3 || isLeadingZero)
            {
                return ValidationOutcome.Invalid(InvalidFormatMessage);
            }

            if (!segment.All(char.IsAsciiDigit) || !byte.TryParse(segment, out _))
            {
                return ValidationOutcome.Invalid(InvalidFormatMessage);
            }
        }

        return ValidationOutcome.Valid();
    }
}
