using Zebra.Printer.Configurator.Core.Configuration;

namespace Zebra.Printer.Configurator.UnitTests.Configuration;

public class WpaPskDeriverTests
{
    [Fact]
    public void DeriveHexPsk_MatchesKnownIeee80211iTestVector()
    {
        // SSID "IEEE" / passphrase "password" is a published IEEE 802.11i Annex H test vector for
        // the PBKDF2-HMAC-SHA1 PSK derivation, used to confirm this implementation matches what
        // every WPA-capable AP/client (and the printer) actually computes from the same inputs.
        var psk = WpaPskDeriver.DeriveHexPsk("IEEE", "password");

        Assert.Equal("f42c6fc52df0ebef9ebb4b90b38a5f902e83fe1b135a70e23aed762e9710a12e", psk);
    }

    [Fact]
    public void DeriveHexPsk_Returns64LowercaseHexCharacters()
    {
        var psk = WpaPskDeriver.DeriveHexPsk("Warehouse-WiFi", "correcthorsebatterystaple");

        Assert.Equal(64, psk.Length);
        Assert.Matches("^[0-9a-f]{64}$", psk);
    }

    [Fact]
    public void DeriveHexPsk_IsDeterministicForTheSameInputs()
    {
        var first = WpaPskDeriver.DeriveHexPsk("Warehouse-WiFi", "correcthorsebatterystaple");
        var second = WpaPskDeriver.DeriveHexPsk("Warehouse-WiFi", "correcthorsebatterystaple");

        Assert.Equal(first, second);
    }

    [Fact]
    public void DeriveHexPsk_DiffersWhenSsidDiffers()
    {
        var first = WpaPskDeriver.DeriveHexPsk("Warehouse-WiFi", "correcthorsebatterystaple");
        var second = WpaPskDeriver.DeriveHexPsk("Different-Ssid", "correcthorsebatterystaple");

        Assert.NotEqual(first, second);
    }
}
