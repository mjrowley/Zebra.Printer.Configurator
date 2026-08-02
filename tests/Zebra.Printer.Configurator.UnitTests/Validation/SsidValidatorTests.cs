using Zebra.Printer.Configurator.Core.Validation;

namespace Zebra.Printer.Configurator.UnitTests.Validation;

public class SsidValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsNullOrWhitespace(string? ssid)
    {
        var result = SsidValidator.Validate(ssid);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsTypicalSsid()
    {
        var result = SsidValidator.Validate("Warehouse-WiFi");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsSsidAtMaxByteLength()
    {
        var result = SsidValidator.Validate(new string('a', 32));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsSsidOverMaxByteLength()
    {
        var result = SsidValidator.Validate(new string('a', 33));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_CountsMultiByteCharactersByUtf8Length()
    {
        // Each '€' is 3 bytes in UTF-8, so 11 of them (33 bytes) exceed the 32-byte limit
        // even though the character count (11) looks well within range.
        var result = SsidValidator.Validate(new string('€', 11));

        Assert.False(result.IsValid);
    }
}
