namespace Zebra.Printer.Configurator.Core.Connectivity;

/// <summary>
/// Shared, app-wide Bluetooth/WiFi connectivity display state backing the header indicators.
/// Registered as a singleton - same reasoning as PairingSession: this is a single-window app with
/// exactly one target printer's connectivity being tracked at a time.
///
/// Bluetooth reflects the OS-level paired/bonded status - set once when a pairing attempt starts/
/// resolves, and left alone across the individual Bluetooth connections Configure/Restart/Factory
/// Reset/Check Configuration each open and close, rather than flickering for every one of them.
/// WiFi is continuously re-checked in the background for as long as a target IP is known (see
/// IWifiConnectivityMonitor), so it reflects live reachability rather than a one-time result.
/// </summary>
public sealed class PrinterConnectivityMonitor
{
    public ConnectionIndicatorState Bluetooth { get; private set; } = ConnectionIndicatorState.Disconnected;

    public ConnectionIndicatorState Wifi { get; private set; } = ConnectionIndicatorState.Disconnected;

    public event EventHandler? Changed;

    public void SetBluetooth(ConnectionIndicatorState state)
    {
        if (Bluetooth == state)
        {
            return;
        }

        Bluetooth = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetWifi(ConnectionIndicatorState state)
    {
        if (Wifi == state)
        {
            return;
        }

        Wifi = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to both indicators grey - a new pairing attempt has no target printer yet.</summary>
    public void Reset()
    {
        SetBluetooth(ConnectionIndicatorState.Disconnected);
        SetWifi(ConnectionIndicatorState.Disconnected);
    }
}
