using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Parsing;

/// <summary>
/// Parses a Zebra printer's NFC NDEF tag payload into a <see cref="PrinterDevice"/>. The field
/// markers/offsets mirror Zebra's own LinkOS-Android-Samples "TapScanConnectTCPBT" sample
/// (<c>findMacAddr</c>) exactly, including its apparent asymmetry between the Bluetooth MAC skip
/// (4, the marker's full length) and the serial/WiFi-MAC skips (3, one less than their marker
/// length) - that quirk is preserved rather than "fixed" since it reflects the real, field-tested
/// parsing of actual printer tag payloads.
/// </summary>
public static class NfcPrinterTagParser
{
    private const string BluetoothMacMarker = "&mB=";
    private const int BluetoothMacSkip = 4;
    private const int BluetoothMacLength = 12;

    private const string SerialMarker = "&s";
    private const int SerialSkip = 3;
    private const int SerialLength = 14;

    private const string WifiMacMarker = "&mW=";
    private const int WifiMacSkip = 3;
    private const int WifiMacLength = 13;

    /// <summary>
    /// Returns the parsed device, or null if the payload doesn't contain a recognizable
    /// Bluetooth MAC address (the one field required to open a BluetoothConnection).
    /// </summary>
    public static PrinterDevice? TryParse(string? ndefPayload)
    {
        if (string.IsNullOrEmpty(ndefPayload))
        {
            return null;
        }

        var bluetoothMac = ExtractField(ndefPayload, BluetoothMacMarker, BluetoothMacSkip, BluetoothMacLength);
        if (bluetoothMac is null)
        {
            return null;
        }

        return new PrinterDevice
        {
            BluetoothMacAddress = bluetoothMac,
            SerialNumber = ExtractField(ndefPayload, SerialMarker, SerialSkip, SerialLength),
            WifiMacAddress = ExtractField(ndefPayload, WifiMacMarker, WifiMacSkip, WifiMacLength),
        };
    }

    private static string? ExtractField(string payload, string marker, int skip, int length)
    {
        var markerIndex = payload.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var start = markerIndex + skip;
        if (start + length > payload.Length)
        {
            return null;
        }

        return payload.Substring(start, length);
    }
}
