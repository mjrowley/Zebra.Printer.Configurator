using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Reads and toggles the printer's web interface (ip.https.enable / ip.http.enable), each a plain
/// literal "on"/"off" SGD value - the same boolean convention this app already uses for
/// wlan.enable/wlan.ip.default_addr_enable (see WlanConfigurationCommandBuilder). Setting both
/// keys doesn't take effect until the printer restarts, so RestartPrinterAsync exists as a
/// separate, later action the caller triggers once the user actually confirms they want to
/// restart now.
/// </summary>
public sealed class LinkOsWebInterfaceService(
    IBluetoothPermissionService bluetoothPermissionService,
    IPrinterConnectionModeProvider connectionModeProvider,
    IAppLog appLog) : IWebInterfaceService
{
    // "device.reset" commonly never sends a response at all, so the default SGD.DO read timeout
    // (tuned for commands that always answer) just stalls the restart for no reason - same
    // reasoning and values as LinkOsPrinterConfigurationService's own reset commands.
    private const int ResetReadTimeoutMs = 3000;
    private const int ResetTimeToWaitForMoreDataMs = 500;

    public async Task<WebInterfaceState> ReadStateAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log("Checking web interface status...");
        var state = await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            var httpsEnabled = SGD.GET("ip.https.enable", connection) == "on";
            var httpEnabled = SGD.GET("ip.http.enable", connection) == "on";
            return new WebInterfaceState { HttpsEnabled = httpsEnabled, HttpEnabled = httpEnabled };
        }, appLog, cancellation: null, cancellationToken);

        appLog.Log($"Web interface is currently {(state.BothEnabled ? "enabled" : "disabled")}.");
        return state;
    }

    public async Task SetEnabledAsync(PrinterDevice device, bool enabled, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        var value = enabled ? "on" : "off";
        appLog.Log($"{(enabled ? "Enabling" : "Disabling")} web interface...");
        await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            SGD.SET("ip.https.enable", value, connection);
            SGD.SET("ip.http.enable", value, connection);
        }, appLog, cancellation: null, cancellationToken);

        appLog.Log($"Web interface {(enabled ? "enabled" : "disabled")} - requires a restart to take effect.", LogLevel.Warning);
    }

    public async Task RestartPrinterAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log("Restarting printer...");
        await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            SGD.DO("device.reset", string.Empty, connection, ResetReadTimeoutMs, ResetTimeToWaitForMoreDataMs);
        }, appLog, cancellation: null, cancellationToken);

        appLog.Log("Restart command sent.", LogLevel.Success);
    }
}
