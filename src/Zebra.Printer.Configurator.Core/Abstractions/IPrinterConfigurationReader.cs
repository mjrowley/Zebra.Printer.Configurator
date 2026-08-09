using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Reads the printer's current WLAN-related SGD settings over Bluetooth, for on-demand display in
/// the UI (the "Check Configuration" action) - independent of the automatic post-restart
/// connectivity test, and usable any time a printer is reachable over Bluetooth.
/// </summary>
public interface IPrinterConfigurationReader
{
    /// <summary>
    /// allowBleFallback defaults to true, but is passed false by the automatic post-pairing WiFi
    /// check on Pairing.razor - a BLE fallback attempted shortly after a fresh Bluetooth bond can
    /// silently trigger a second, unexpected OS pairing dialog (see PrinterConnectionRunner's own
    /// doc comment). The user-triggered "Check Configuration" button keeps the default.
    /// </summary>
    Task<IReadOnlyList<PrinterConfigurationValue>> ReadConfigurationAsync(PrinterDevice device, bool allowBleFallback = true, CancellationToken cancellationToken = default);
}
