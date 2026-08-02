using Zebra.Printer.Configurator.Core.Validation;

namespace Zebra.Printer.Configurator.UnitTests.Validation;

public class WifiPasswordValidatorTests
{
    [Fact]
    public void Validate_AcceptsEmptyPasswordAsOpenNetwork()
    {
        var result = WifiPasswordValidator.Validate(string.Empty);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsNullPasswordAsOpenNetwork()
    {
        var result = WifiPasswordValidator.Validate(null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsPasswordBelowMinimumLength()
    {
        var result = WifiPasswordValidator.Validate("1234567");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsPasswordAboveMaximumLength()
    {
        var result = WifiPasswordValidator.Validate(new string('a', 64));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("correcthorsebatterystaple")]
    public void Validate_AcceptsPasswordWithinRange(string password)
    {
        var result = WifiPasswordValidator.Validate(password);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsPasswordAtMaximumLength()
    {
        var result = WifiPasswordValidator.Validate(new string('a', 63));

        Assert.True(result.IsValid);
    }
}
