namespace Zebra.Printer.Configurator.Core.Configuration;

/// <summary>
/// The WLAN-related SGD keys read back for diagnostics - shared between the post-restart
/// connectivity-test failure path (LinkOsConnectivityTestService) and the on-demand "Check
/// Configuration" UI action (IPrinterConfigurationReader), so both surface the same set of values.
/// </summary>
public static class WlanDiagnosticKeys
{
    // wlan.wpa.psk is included so a diagnosing user can at least see whether *something* was
    // stored (length > 0) without the actual WiFi password appearing on screen.
    public static readonly IReadOnlyList<string> All =
    [
        "wlan.enable",
        "wlan.security",
        "wlan.essid",
        "wlan.wpa.psk",
        "wlan.ip.protocol",
        "wlan.ip.default_addr_enable",
        "wlan.ip.addr",
        "wlan.ip.netmask",
        "wlan.ip.gateway",
        "wlan.state",
    ];
}
