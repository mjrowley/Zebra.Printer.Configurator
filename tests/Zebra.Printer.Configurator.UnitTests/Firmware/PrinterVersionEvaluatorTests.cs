using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UnitTests.Firmware;

public class PrinterVersionEvaluatorTests
{
    private static readonly FirmwareBundle Bundle = new()
    {
        ModelName = "ZD421",
        ExpectedLinkOsVersion = new LinkOsVersion(7, 6, 2),
        ExpectedFirmwareVersion = "V93.21.49Z",
        FirmwareAssetLogicalPath = "ZD421_Firmware/V93.21.49Z.zpl",
    };

    [Fact]
    public void Evaluate_BothMatchExactly_ReturnsUpToDate()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.6.2", "V93.21.49Z");

        Assert.Equal(PrinterVersionOutcome.UpToDate, result.Outcome);
        Assert.Same(Bundle, result.Bundle);
    }

    [Fact]
    public void Evaluate_BothHigher_ReturnsNewerThanExpected()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.7.0", "V93.22.01Z");

        Assert.Equal(PrinterVersionOutcome.NewerThanExpected, result.Outcome);
    }

    [Fact]
    public void Evaluate_BothLower_ReturnsNeedsUpdate()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.5.0", "V93.21.06Z");

        Assert.Equal(PrinterVersionOutcome.NeedsUpdate, result.Outcome);
    }

    [Fact]
    public void Evaluate_LinkOsHigherFirmwareLower_ReturnsNewerThanExpected()
    {
        // Per spec precedence: "higher" applies whenever ANY dimension is higher, regardless of the
        // other dimension - "needs update" is explicitly gated on "and neither is higher".
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.7.0", "V93.21.06Z");

        Assert.Equal(PrinterVersionOutcome.NewerThanExpected, result.Outcome);
    }

    [Fact]
    public void Evaluate_LinkOsLowerFirmwareHigher_ReturnsNewerThanExpected()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.5.0", "V93.22.01Z");

        Assert.Equal(PrinterVersionOutcome.NewerThanExpected, result.Outcome);
    }

    [Fact]
    public void Evaluate_OnlyLinkOsLower_ReturnsNeedsUpdate()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.5.0", "V93.21.49Z");

        Assert.Equal(PrinterVersionOutcome.NeedsUpdate, result.Outcome);
    }

    [Fact]
    public void Evaluate_OnlyFirmwareLower_ReturnsNeedsUpdate()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.6.2", "V93.21.06Z");

        Assert.Equal(PrinterVersionOutcome.NeedsUpdate, result.Outcome);
    }

    [Fact]
    public void Evaluate_NullBundle_ReturnsUnsupported()
    {
        var result = PrinterVersionEvaluator.Evaluate(null, "7.6.2", "V93.21.49Z");

        Assert.Equal(PrinterVersionOutcome.Unsupported, result.Outcome);
        Assert.Null(result.Bundle);
        Assert.Equal("7.6.2", result.LinkOsVersionFound);
    }

    [Fact]
    public void Evaluate_DifferentFirmwareBranch_ReturnsUnsupported()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.6.2", "V100.5.2Z");

        Assert.Equal(PrinterVersionOutcome.Unsupported, result.Outcome);
    }

    [Fact]
    public void Evaluate_UnparseableVersionStrings_ReturnsUnsupported()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "not a version", "also not a version");

        Assert.Equal(PrinterVersionOutcome.Unsupported, result.Outcome);
    }

    [Fact]
    public void Evaluate_PreservesFoundVersionStringsOnEveryOutcome()
    {
        var result = PrinterVersionEvaluator.Evaluate(Bundle, "7.5.0", "V93.21.06Z");

        Assert.Equal("7.5.0", result.LinkOsVersionFound);
        Assert.Equal("V93.21.06Z", result.FirmwareVersionFound);
    }
}

public class FirmwareBundleCatalogTests
{
    [Theory]
    [InlineData("ZD421")]
    [InlineData("ZD421-D9J254516544")]
    [InlineData("zd421")]
    public void FindByProductName_MatchesZd421CaseInsensitiveSubstring(string productName)
    {
        var bundle = FirmwareBundleCatalog.FindByProductName(productName);

        Assert.NotNull(bundle);
        Assert.Equal("ZD421", bundle!.ModelName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ZD621")]
    [InlineData("ZQ630 Plus")]
    public void FindByProductName_ReturnsNullForUnknownOrMissingModel(string? productName)
    {
        var bundle = FirmwareBundleCatalog.FindByProductName(productName);

        Assert.Null(bundle);
    }
}
