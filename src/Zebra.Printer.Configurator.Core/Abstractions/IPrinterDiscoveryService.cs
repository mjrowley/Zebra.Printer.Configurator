using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Discovers a printer via NFC tap. Implemented on Android using NfcAdapter foreground dispatch;
/// <see cref="StartListening"/>/<see cref="StopListening"/> map to registering/unregistering that
/// dispatch for the lifetime of the pairing screen.
/// </summary>
public interface IPrinterDiscoveryService
{
    event EventHandler<PrinterDevice>? PrinterDiscovered;

    void StartListening();

    void StopListening();
}
