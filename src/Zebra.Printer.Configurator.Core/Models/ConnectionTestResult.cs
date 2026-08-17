namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// Outcome of polling the printer after restart to confirm it rejoined the target WiFi network.
/// </summary>
public sealed record ConnectionTestResult
{
    public required bool Success { get; init; }

    public string? ConfirmedWlanState { get; init; }

    public string? FailureReason { get; init; }

    // The IP address the printer actually ended up reachable/expected at - for a Static
    // configuration this is always known (the user-entered StaticIpAddress), for Dhcp it's
    // whatever the printer reports back post-restart (null if that was never discovered, e.g. a
    // failed DHCP test). Callers that need "the printer's current WiFi IP" downstream (Result.razor's
    // display, firmware-check/Reconfigure's probe, Progress.razor's WifiMonitor) should read this
    // rather than WlanConfiguration.StaticIpAddress, which is meaningless for Dhcp.
    public string? ResolvedIpAddress { get; init; }

    public static ConnectionTestResult Succeeded(string wlanState, string resolvedIpAddress) =>
        new() { Success = true, ConfirmedWlanState = wlanState, ResolvedIpAddress = resolvedIpAddress };

    public static ConnectionTestResult Failed(string reason, string? resolvedIpAddress = null) =>
        new() { Success = false, FailureReason = reason, ResolvedIpAddress = resolvedIpAddress };
}
