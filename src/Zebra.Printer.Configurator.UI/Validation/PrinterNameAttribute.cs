using System.ComponentModel.DataAnnotations;
using Zebra.Printer.Configurator.Core.Validation;

namespace Zebra.Printer.Configurator.UI.Validation;

/// <summary>Thin DataAnnotations wrapper so Configure.razor's EditForm can use Core's PrinterNameValidator as the single source of truth.</summary>
public sealed class PrinterNameAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var outcome = PrinterNameValidator.Validate(value as string);
        if (outcome.IsValid)
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(outcome.ErrorMessage, [validationContext.MemberName!]);
    }
}
