namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Which transport SGD/ZPL commands to the target printer currently go over. Every function that
/// talks to the printer (apply configuration, restart, factory reset, check configuration) reads
/// this rather than hard-coding Bluetooth, so switching modes (see "Connect via WiFi") changes all
/// of them at once.
/// </summary>
public enum PrinterConnectionMode
{
    Bluetooth,
    Wifi,
}

public interface IPrinterConnectionModeProvider
{
    PrinterConnectionMode Mode { get; }

    /// <summary>The printer's WiFi IP address to connect to - set together with <see cref="UseWifi"/>, null otherwise.</summary>
    string? WifiIpAddress { get; }

    event EventHandler? Changed;

    void UseBluetooth();

    void UseWifi(string ipAddress);
}
