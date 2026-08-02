namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// A Zebra printer identified via an NFC tap. <see cref="BluetoothMacAddress"/> comes from the
/// printer's NFC NDEF tag payload and is used to open the initial <c>BluetoothConnection</c> for
/// configuration, since the printer isn't on the target WiFi network yet at this point.
/// </summary>
public sealed record PrinterDevice
{
    public required string BluetoothMacAddress { get; init; }

    public string? SerialNumber { get; init; }

    public string? WifiMacAddress { get; init; }
}
