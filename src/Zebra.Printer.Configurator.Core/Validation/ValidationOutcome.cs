namespace Zebra.Printer.Configurator.Core.Validation;

public sealed record ValidationOutcome(bool IsValid, string? ErrorMessage)
{
    public static ValidationOutcome Valid() => new(true, null);

    public static ValidationOutcome Invalid(string errorMessage) => new(false, errorMessage);
}
