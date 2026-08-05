namespace Zebra.Printer.Configurator.Core.Connectivity;

/// <summary>
/// The four states shown by a header connectivity indicator (Bluetooth or WiFi), each with its own
/// color in the UI: Disconnected = grey, Connecting = orange, Error = red, Connected = green.
/// </summary>
public enum ConnectionIndicatorState
{
    Disconnected,
    Connecting,
    Error,
    Connected,
}
