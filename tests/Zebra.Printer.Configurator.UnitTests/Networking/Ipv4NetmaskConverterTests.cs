using Zebra.Printer.Configurator.Core.Networking;

namespace Zebra.Printer.Configurator.UnitTests.Networking;

public class Ipv4NetmaskConverterTests
{
    [Theory]
    [InlineData(0, "0.0.0.0")]
    [InlineData(8, "255.0.0.0")]
    [InlineData(16, "255.255.0.0")]
    [InlineData(23, "255.255.254.0")]
    [InlineData(24, "255.255.255.0")]
    [InlineData(25, "255.255.255.128")]
    [InlineData(30, "255.255.255.252")]
    [InlineData(32, "255.255.255.255")]
    public void FromPrefixLength_ReturnsExpectedDottedDecimalMask(int prefixLength, string expected)
    {
        var result = Ipv4NetmaskConverter.FromPrefixLength(prefixLength);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void FromPrefixLength_ThrowsForOutOfRangeValues(int prefixLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ipv4NetmaskConverter.FromPrefixLength(prefixLength));
    }
}
