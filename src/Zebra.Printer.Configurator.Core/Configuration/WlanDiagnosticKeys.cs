namespace Zebra.Printer.Configurator.Core.Configuration;

/// <summary>
/// The SGD keys read back for diagnostics - shared between the post-restart connectivity-test
/// failure path (LinkOsConnectivityTestService) and the on-demand "Check Configuration" UI action
/// (IPrinterConfigurationReader), so both surface the same set of values. Despite the name (kept for
/// the WLAN keys that were here first), this now also covers non-WLAN keys this app configures or
/// cares about - apl.enable (PDF Direct) and the fixed ZD421 printer defaults from
/// PrinterDefaultsCommandBuilder.
/// </summary>
public static class WlanDiagnosticKeys
{
    // wlan.wpa.psk is included so a diagnosing user can at least see whether *something* was
    // stored (length > 0) without the actual WiFi password appearing on screen.
    //
    // Ordered alphabetically by key, with device.friendly_name pinned first (it's the one value a
    // human reads this list to identify "which printer is this", so it stays at the top regardless
    // of where it'd otherwise sort) - CheckConfigurationResults.razor renders this list in source
    // order, so the order here IS the display order.
    public static readonly IReadOnlyList<string> All =
    [
        "device.friendly_name",
        "apl.enable",
        "apl.settings",
        "ezpl.label_length_max",
        "ezpl.media_type",
        "ezpl.print_method",
        "ezpl.print_width",
        "ip.dhcp.enable",
        "media.printmode",
        "wlan.enable",
        "wlan.essid",
        "wlan.ip.addr",
        "wlan.ip.default_addr_enable",
        "wlan.ip.gateway",
        "wlan.ip.netmask",
        "wlan.ip.protocol",
        "wlan.security",
        "wlan.state",
        "wlan.wpa.psk",
        "zpl.left_position",
    ];
}
