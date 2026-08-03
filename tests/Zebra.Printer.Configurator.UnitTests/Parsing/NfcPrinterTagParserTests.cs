using Zebra.Printer.Configurator.Core.Parsing;

namespace Zebra.Printer.Configurator.UnitTests.Parsing;

public class NfcPrinterTagParserTests
{
    // Field offsets/skips mirror Zebra's own TapScanConnectTCPBT sample exactly - see
    // NfcPrinterTagParser's doc comment for why the WiFi-MAC/serial skips look asymmetric.
    private const string FullPayload = "91031503&mB=AABBCCDDEEFF&sN12345678901234&mW=001122334455TAIL";

    [Fact]
    public void TryParse_ExtractsBluetoothMacAddress_InColonSeparatedForm()
    {
        // The tag encodes a bare 12-hex-digit string, but Android's BluetoothAdapter/
        // BluetoothConnection both require the standard colon-separated form - passing the raw
        // form throws "<address> is not a valid Bluetooth address" from Android itself.
        var device = NfcPrinterTagParser.TryParse(FullPayload);

        Assert.NotNull(device);
        Assert.Equal("AA:BB:CC:DD:EE:FF", device!.BluetoothMacAddress);
    }

    [Fact]
    public void TryParse_ExtractsSerialNumber()
    {
        var device = NfcPrinterTagParser.TryParse(FullPayload);

        Assert.Equal("12345678901234", device!.SerialNumber);
    }

    [Fact]
    public void TryParse_ExtractsWifiMacAddress()
    {
        var device = NfcPrinterTagParser.TryParse(FullPayload);

        // The marker skip (3) lands one character before the end of "&mW=", so the extracted
        // value includes the leading '=' - this mirrors the vendor sample's own behavior exactly.
        Assert.Equal("=001122334455", device!.WifiMacAddress);
    }

    [Fact]
    public void TryParse_ReturnsDeviceWithOnlyBluetoothMac_WhenSerialAndWifiMarkersAbsent()
    {
        var device = NfcPrinterTagParser.TryParse("&mB=AABBCCDDEEFF");

        Assert.NotNull(device);
        Assert.Equal("AA:BB:CC:DD:EE:FF", device!.BluetoothMacAddress);
        Assert.Null(device.SerialNumber);
        Assert.Null(device.WifiMacAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no markers here")]
    public void TryParse_ReturnsNull_WhenBluetoothMacMarkerMissing(string? payload)
    {
        var device = NfcPrinterTagParser.TryParse(payload);

        Assert.Null(device);
    }

    [Fact]
    public void TryParse_ReturnsNull_WhenBluetoothMacIsTruncated()
    {
        // Marker present but fewer than 12 characters follow it.
        var device = NfcPrinterTagParser.TryParse("&mB=AABB");

        Assert.Null(device);
    }

    [Fact]
    public void TryParse_ReturnsNull_WhenBluetoothMacContainsNonHexCharacters()
    {
        var device = NfcPrinterTagParser.TryParse("&mB=AABBCCDDEEZZ");

        Assert.Null(device);
    }

    [Fact]
    public void TryParse_ExtractsBluetoothMacAddress_FromMblMarker()
    {
        // Reported directly from an on-device NDEF payload dump for a ZD621 (whose tag uses
        // "&mBL=" rather than the "&mB=" the ZD421/ZQ630 Plus use):
        const string payload = ".zebra.com/apps/r/nfc?mE=60953260083d&mW=000000000000&mBL=6095325ef47e&c=ZD6A042-D0PF00EZ&s=D9J254516544&v=0";

        var device = NfcPrinterTagParser.TryParse(payload);

        Assert.NotNull(device);
        Assert.Equal("60:95:32:5E:F4:7E", device!.BluetoothMacAddress);
    }

    [Fact]
    public void TryParse_PrefersMbMarker_WhenBothMbAndMblArePresent()
    {
        var device = NfcPrinterTagParser.TryParse("&mB=AABBCCDDEEFF&mBL=112233445566");

        Assert.Equal("AA:BB:CC:DD:EE:FF", device!.BluetoothMacAddress);
    }
}
