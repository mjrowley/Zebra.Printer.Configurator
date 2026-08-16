using Zebra.Printer.Configurator.Core.Configuration;

namespace Zebra.Printer.Configurator.UnitTests.Configuration;

public class ConfigurationValueMatcherTests
{
    [Fact]
    public void Evaluate_ReturnsInformational_WhenNoExpectedValue()
    {
        var result = ConfigurationValueMatcher.Evaluate("wlan.state", expected: null, actual: "CONNECTED");

        Assert.Equal(ConfigurationValueMatch.Informational, result);
    }

    [Fact]
    public void Evaluate_ReturnsMatches_WhenActualEqualsExpected()
    {
        var result = ConfigurationValueMatcher.Evaluate("ezpl.print_width", expected: "799", actual: "799");

        Assert.Equal(ConfigurationValueMatch.Matches, result);
    }

    [Fact]
    public void Evaluate_ReturnsMismatch_WhenActualDiffersFromExpected()
    {
        var result = ConfigurationValueMatcher.Evaluate("ezpl.print_width", expected: "799", actual: "812");

        Assert.Equal(ConfigurationValueMatch.Mismatch, result);
    }

    [Fact]
    public void Evaluate_TreatsMaskedPskReadbackAsAMatch()
    {
        // getvar on wlan.wpa.psk always returns "*" regardless of what's actually stored - the
        // 64-hex-digit PSK itself is never echoed back, so "*" IS the confirmation, not a mismatch.
        var result = ConfigurationValueMatcher.Evaluate("wlan.wpa.psk", expected: "a1b2c3", actual: "*");

        Assert.Equal(ConfigurationValueMatch.Matches, result);
    }

    [Fact]
    public void Evaluate_TreatsAnyNonMaskedPskReadbackAsAMismatch()
    {
        var result = ConfigurationValueMatcher.Evaluate("wlan.wpa.psk", expected: "a1b2c3", actual: "a1b2c3");

        Assert.Equal(ConfigurationValueMatch.Mismatch, result);
    }

    [Fact]
    public void DeferredVerificationKeys_ContainsOnlyTheTwoOperationalIpKeys()
    {
        Assert.Equal(["wlan.ip.addr", "wlan.ip.gateway"], ConfigurationValueMatcher.DeferredVerificationKeys);
    }
}
