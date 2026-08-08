using Zebra.Printer.Configurator.Core.Firmware;

namespace Zebra.Printer.Configurator.UnitTests.Firmware;

public class FirmwareVersionTests
{
    [Fact]
    public void TryParse_ParsesBranchMajorMinorSuffix()
    {
        var parsed = FirmwareVersion.TryParse("V93.21.49Z", out var version);

        Assert.True(parsed);
        Assert.Equal(new FirmwareVersion(93, 21, 49, "Z"), version);
    }

    [Fact]
    public void TryParse_AllowsEmptySuffix()
    {
        var parsed = FirmwareVersion.TryParse("V93.21.49", out var version);

        Assert.True(parsed);
        Assert.Equal("", version.Suffix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("93.21.49Z")]
    [InlineData("V93.21")]
    [InlineData("not a version")]
    public void TryParse_RejectsMalformedInput(string? value)
    {
        var parsed = FirmwareVersion.TryParse(value, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void ToString_FormatsAsVBranchDotMajorDotMinorSuffix()
    {
        Assert.Equal("V93.21.49Z", new FirmwareVersion(93, 21, 49, "Z").ToString());
    }
}

public class FirmwareVersionComparerTests
{
    private static FirmwareVersion Parse(string value)
    {
        Assert.True(FirmwareVersion.TryParse(value, out var version));
        return version;
    }

    [Fact]
    public void Compare_SameBranchHigherMinor_ReturnsNewer()
    {
        // Confirmed sequential ordering from the bundled Link-OS 7.6.2 release notes.
        var result = FirmwareVersionComparer.Compare(Parse("V93.21.49Z"), Parse("V93.21.48Z"));

        Assert.Equal(FirmwareVersionComparison.Newer, result);
    }

    [Fact]
    public void Compare_SameBranchLowerMinor_ReturnsOlder()
    {
        var result = FirmwareVersionComparer.Compare(Parse("V93.21.06Z"), Parse("V93.21.49Z"));

        Assert.Equal(FirmwareVersionComparison.Older, result);
    }

    [Fact]
    public void Compare_HigherMajorBeatsLowerMinor_ReturnsNewer()
    {
        var result = FirmwareVersionComparer.Compare(Parse("V93.22.01Z"), Parse("V93.21.49Z"));

        Assert.Equal(FirmwareVersionComparison.Newer, result);
    }

    [Fact]
    public void Compare_SameVersion_ReturnsEqual()
    {
        var result = FirmwareVersionComparer.Compare(Parse("V93.21.49Z"), Parse("V93.21.49Z"));

        Assert.Equal(FirmwareVersionComparison.Equal, result);
    }

    [Fact]
    public void Compare_DifferentBranch_ReturnsIncomparable()
    {
        // V100/V101 are entirely different OS branches per the release notes - not numerically
        // comparable to V93, regardless of which number is larger.
        var result = FirmwareVersionComparer.Compare(Parse("V100.5.2Z"), Parse("V93.21.49Z"));

        Assert.Equal(FirmwareVersionComparison.Incomparable, result);
    }

    [Fact]
    public void Compare_IgnoresSuffixWhenBranchMajorMinorMatch()
    {
        var result = FirmwareVersionComparer.Compare(Parse("V93.21.49Z"), Parse("V93.21.49A"));

        Assert.Equal(FirmwareVersionComparison.Equal, result);
    }
}
