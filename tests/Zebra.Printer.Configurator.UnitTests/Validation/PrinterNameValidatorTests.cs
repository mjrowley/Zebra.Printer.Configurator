using Zebra.Printer.Configurator.Core.Validation;

namespace Zebra.Printer.Configurator.UnitTests.Validation;

public class PrinterNameValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsNullOrWhitespace(string? printerName)
    {
        var result = PrinterNameValidator.Validate(printerName);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsTypicalPrinterName()
    {
        var result = PrinterNameValidator.Validate("ZD421");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsACustomName()
    {
        var result = PrinterNameValidator.Validate("Warehouse Printer 3");

        Assert.True(result.IsValid);
    }
}
