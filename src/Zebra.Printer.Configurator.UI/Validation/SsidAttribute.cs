using System.ComponentModel.DataAnnotations;
using Zebra.Printer.Configurator.Core.Validation;

namespace Zebra.Printer.Configurator.UI.Validation;

/// <summary>Thin DataAnnotations wrapper so Configure.razor's EditForm can use Core's SsidValidator as the single source of truth.</summary>
public sealed class SsidAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var outcome = SsidValidator.Validate(value as string);
        if (outcome.IsValid)
        {
            return ValidationResult.Success;
        }

        // Without memberNames, Blazor's EditContext files this under the whole-model field
        // identifier rather than the specific property, so ValidationMessage For="...Ssid" never
        // finds it - only a ValidationSummary would.
        return new ValidationResult(outcome.ErrorMessage, [validationContext.MemberName!]);
    }
}
