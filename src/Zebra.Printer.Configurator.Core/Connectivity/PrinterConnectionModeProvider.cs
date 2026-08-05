using Zebra.Printer.Configurator.Core.Abstractions;

namespace Zebra.Printer.Configurator.Core.Connectivity;

/// <summary>
/// Shared, app-wide record of which transport (Bluetooth or WiFi) is currently used to talk to the
/// target printer. Singleton, same reasoning as PairingSession/PrinterConnectivityMonitor - one
/// printer, one active transport, at a time.
/// </summary>
public sealed class PrinterConnectionModeProvider : IPrinterConnectionModeProvider
{
    public PrinterConnectionMode Mode { get; private set; } = PrinterConnectionMode.Bluetooth;

    public string? WifiIpAddress { get; private set; }

    public event EventHandler? Changed;

    public void UseBluetooth()
    {
        Mode = PrinterConnectionMode.Bluetooth;
        WifiIpAddress = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UseWifi(string ipAddress)
    {
        Mode = PrinterConnectionMode.Wifi;
        WifiIpAddress = ipAddress;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
