using Zebra.Printer.Configurator.Core.Validation;

namespace Zebra.Printer.Configurator.UnitTests.Validation;

public class IPv4ValidatorTests
{
    [Theory]
    [InlineData("192.168.1.100")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("10.0.0.1")]
    public void Validate_AcceptsValidDottedQuad(string ipAddress)
    {
        var result = IPv4Validator.Validate(ipAddress);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("192.168.1")]
    [InlineData("192.168.1.1.1")]
    [InlineData("192.168.1.256")]
    [InlineData("192.168.1.-1")]
    [InlineData("192.168.01.1")]
    [InlineData("192.168.1.abc")]
    [InlineData("192.168.1. 1")]
    [InlineData("not an ip")]
    public void Validate_RejectsInvalidInput(string? ipAddress)
    {
        var result = IPv4Validator.Validate(ipAddress);

        Assert.False(result.IsValid);
    }
}
