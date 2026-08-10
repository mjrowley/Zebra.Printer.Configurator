using Zebra.Printer.Configurator.Core.Firmware;

namespace Zebra.Printer.Configurator.UnitTests.Firmware;

public class LinkOsVersionTests
{
    [Fact]
    public void TryParse_ParsesMajorMinorMicro()
    {
        var parsed = LinkOsVersion.TryParse("7.6.2", out var version);

        Assert.True(parsed);
        Assert.Equal(new LinkOsVersion(7, 6, 2), version);
    }

    [Fact]
    public void TryParse_ParsesMajorMinorWithMicroDefaultingToZero()
    {
        // Confirmed on-device: appl.link_os_version_full doesn't always include the micro component -
        // a printer running 7.5.0 reported plain "7.5".
        var parsed = LinkOsVersion.TryParse("7.5", out var version);

        Assert.True(parsed);
        Assert.Equal(new LinkOsVersion(7, 5, 0), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7.6.2.1")]
    [InlineData("V7.6.2")]
    [InlineData("not a version")]
    public void TryParse_RejectsMalformedInput(string? value)
    {
        var parsed = LinkOsVersion.TryParse(value, out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData(7, 6, 2, 7, 6, 1, 1)]   // higher micro
    [InlineData(7, 6, 2, 7, 5, 9, 1)]   // higher minor beats lower micro
    [InlineData(7, 6, 2, 6, 9, 9, 1)]   // higher major beats lower minor/micro
    [InlineData(7, 6, 2, 7, 6, 2, 0)]   // equal
    [InlineData(7, 6, 1, 7, 6, 2, -1)]  // lower micro
    public void CompareTo_OrdersByMajorThenMinorThenMicro(int aMajor, int aMinor, int aMicro, int bMajor, int bMinor, int bMicro, int expectedSign)
    {
        var a = new LinkOsVersion(aMajor, aMinor, aMicro);
        var b = new LinkOsVersion(bMajor, bMinor, bMicro);

        Assert.Equal(expectedSign, Math.Sign(a.CompareTo(b)));
    }

    [Fact]
    public void ToString_FormatsAsMajorDotMinorDotMicro()
    {
        Assert.Equal("7.6.2", new LinkOsVersion(7, 6, 2).ToString());
    }
}
