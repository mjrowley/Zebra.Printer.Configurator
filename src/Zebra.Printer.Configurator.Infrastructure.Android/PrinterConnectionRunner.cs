using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Sdk.Comm;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Opens whichever connection the current IPrinterConnectionModeProvider selects - Bluetooth or a
/// plain TcpConnection to the printer's WiFi IP - so every function that talks to the printer
/// (apply configuration, restart, factory reset, check configuration) automatically obeys the
/// active transport instead of each one hard-coding Bluetooth.
///
/// The Bluetooth path itself cascades from Classic to Low Energy: if BluetoothConnectionRunner
/// exhausts its own retries, BleConnectionRunner is tried as a last resort before giving up -
/// mirroring Zebra's own Printer Setup Utility's documented connection search order (network, then
/// Bluetooth Classic, then Bluetooth LE) at the transport layer, for every caller automatically.
///
/// The WiFi TCP port defaults to DefaultSgdPort (general SGD traffic) - callers that are actually
/// pushing a file to the printer (SendFileContents) should pass FileTransferSgdPort explicitly
/// instead; see both constants' own doc comments for why they differ.
/// </summary>
internal static class PrinterConnectionRunner
{
    // Confirmed via direct on-device port testing against the printer (2026-08-19): general SGD
    // get/set/do traffic (apply configuration, restart, factory reset, calibration, status/version
    // reads, web interface toggle, file *listing*) only responds reliably on 9100 - 6101 is reserved
    // for actual file transfers (SendFileContents), which is where FileTransferSgdPort below is used
    // explicitly instead. Do not use 6101 as the default for a new caller unless it's genuinely
    // pushing a file to the printer.
    private const int DefaultSgdPort = 9100;

    // See LinkOsFirmwareUpdateService's own doc comment for why 6101 (not 9100) is used for large
    // binary transfers specifically - 9100 was observed on-device throttling a 41MB firmware transfer
    // to ~10 minutes, where 6101 proved fast and reliable. Only the callers that actually push a file
    // via SendFileContents should pass this explicitly (LinkOsBagTagTemplateService.DeployTemplatesAsync
    // today) - everything else should use the DefaultSgdPort above.
    public const int FileTransferSgdPort = 6101;

    public static Task RunAsync(PrinterDevice device, IPrinterConnectionModeProvider connectionModeProvider, Action<Connection> action, IAppLog appLog, PrinterOperationCancellation? cancellation, CancellationToken cancellationToken, bool allowBleFallback = true, int wifiPort = DefaultSgdPort) =>
        RunAsync<object?>(device, connectionModeProvider, connection =>
        {
            action(connection);
            return null;
        }, appLog, cancellation, cancellationToken, allowBleFallback, wifiPort);

    public static async Task<T> RunAsync<T>(PrinterDevice device, IPrinterConnectionModeProvider connectionModeProvider, Func<Connection, T> func, IAppLog appLog, PrinterOperationCancellation? cancellation, CancellationToken cancellationToken, bool allowBleFallback = true, int wifiPort = DefaultSgdPort)
    {
        var connection = await OpenAsync(device, connectionModeProvider, appLog, cancellationToken, allowBleFallback, wifiPort);
        using var _ = cancellation?.TrackActiveConnection(connection.Close);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    return func(connection);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }, cancellationToken);
        }
        finally
        {
            // Confirmed on-device (adb logcat): a finally block resumes on whatever context the
            // preceding await captured, which for a Blazor-component-initiated call is the UI
            // thread's SynchronizationContext - a plain synchronous Close() here ran ON the UI
            // thread and blocked it for several seconds (Android's own Choreographer logged
            // "Skipped 601 frames!" and a 5020ms "Davey!" at the exact same moment), stalling the
            // whole page including CSS animations like the spinner. Task.Run puts it back on a
            // background thread, matching where Open()/func() already ran.
            await Task.Run(connection.Close);
        }
    }

    /// <summary>
    /// Opens a connection using whichever transport IPrinterConnectionModeProvider currently
    /// selects, without running anything against it or closing it - the caller owns its lifecycle
    /// from here. Used directly by PrinterConnectionSessionFactory so several steps can share one
    /// connection instead of each independently reconnecting; RunAsync above is built on top of this
    /// for the simpler one-shot callers.
    ///
    /// allowBleFallback defaults to true (Zebra's own Printer Setup Utility connection search order:
    /// network, then Bluetooth Classic, then Bluetooth LE) but is passed false by callers made
    /// shortly after a fresh OS-level Bluetooth bond - opening a BluetoothLeConnection to a device
    /// that's never been BLE-bonded silently triggers Android's own BLE pairing negotiation, showing
    /// as a second, unexpected system pairing dialog with its own code right on the heels of the
    /// Classic one.
    /// </summary>
    public static Task<Connection> OpenAsync(PrinterDevice device, IPrinterConnectionModeProvider connectionModeProvider, IAppLog appLog, CancellationToken cancellationToken, bool allowBleFallback = true, int wifiPort = DefaultSgdPort)
    {
        if (connectionModeProvider.Mode == PrinterConnectionMode.Wifi && connectionModeProvider.WifiIpAddress is not null)
        {
            return OpenWifiAsync(connectionModeProvider.WifiIpAddress, wifiPort, cancellationToken);
        }

        return OpenBluetoothAsync(device.BluetoothMacAddress, appLog, cancellationToken, allowBleFallback);
    }

    private static async Task<Connection> OpenBluetoothAsync(string macAddress, IAppLog appLog, CancellationToken cancellationToken, bool allowBleFallback)
    {
        try
        {
            return await BluetoothConnectionRunner.OpenAsync(macAddress, appLog, cancellationToken);
        }
        catch (Exception ex) when (allowBleFallback && ex is not OperationCanceledException)
        {
            appLog.Log($"{ex.Message} Trying Bluetooth Low Energy...", LogLevel.Warning);
            return await BleConnectionRunner.OpenAsync(macAddress, appLog, cancellationToken);
        }
    }

    private static Task<Connection> OpenWifiAsync(string ipAddress, int port, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            Connection connection = new TcpConnection(ipAddress, port);
            connection.Open();
            return connection;
        }, cancellationToken);
}
