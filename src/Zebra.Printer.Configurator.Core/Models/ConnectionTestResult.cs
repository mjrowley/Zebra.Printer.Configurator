namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// Outcome of polling the printer after restart to confirm it rejoined the target WiFi network.
/// </summary>
public sealed record ConnectionTestResult
{
    public required bool Success { get; init; }

    public string? ConfirmedWlanState { get; init; }

    public string? FailureReason { get; init; }

    public static ConnectionTestResult Succeeded(string wlanState) =>
        new() { Success = true, ConfirmedWlanState = wlanState };

    public static ConnectionTestResult Failed(string reason) =>
        new() { Success = false, FailureReason = reason };
}
