using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Workflow;

/// <summary>
/// Carries state between the Pairing -> Configure -> Progress -> Result pages within a single
/// pairing attempt. Registered as a singleton - this is a single-window MAUI Blazor Hybrid app, so
/// there's exactly one attempt in flight at a time.
/// </summary>
public sealed class PairingSession
{
    public PrinterDevice? Device { get; set; }

    public WlanConfiguration? Configuration { get; set; }

    /// <summary>
    /// PrinterDashboard's last successfully loaded WiFi IP + merged status read. Returning to
    /// /dashboard for the same device (e.g. Configure.razor's Back button bouncing back through
    /// Pairing.razor's restore path) reuses this instead of forcing a second full Bluetooth
    /// reconnect + WLAN/status read - each is ~15-20s and needless churn risks retriggering this
    /// printer's known dual BLE/Classic bonding flakiness for no new information. Cleared on
    /// <see cref="Reset"/> (fresh pair) so a new device or a fresh scan always reads for real, and
    /// kept in sync whenever the dashboard's own components (e.g. WebInterfaceTogglePanel) confirm a
    /// newer value than what was last cached.
    /// </summary>
    public DashboardStatusSnapshot? CachedDashboardStatus { get; set; }

    public void Reset()
    {
        Device = null;
        Configuration = null;
        CachedDashboardStatus = null;
    }

    public sealed record DashboardStatusSnapshot(string? WifiIpAddress, PrinterStatus Status);
}
