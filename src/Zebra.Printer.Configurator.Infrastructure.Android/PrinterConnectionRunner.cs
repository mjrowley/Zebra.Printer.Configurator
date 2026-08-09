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
/// </summary>
internal static class PrinterConnectionRunner
{
    private const int SgdPort = 6101;

    public static Task RunAsync(PrinterDevice device, IPrinterConnectionModeProvider connectionModeProvider, Action<Connection> action, IAppLog appLog, PrinterOperationCancellation? cancellation, CancellationToken cancellationToken) =>
        RunAsync<object?>(device, connectionModeProvider, connection =>
        {
            action(connection);
            return null;
        }, appLog, cancellation, cancellationToken);

    public static async Task<T> RunAsync<T>(PrinterDevice device, IPrinterConnectionModeProvider connectionModeProvider, Func<Connection, T> func, IAppLog appLog, PrinterOperationCancellation? cancellation, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(device, connectionModeProvider, appLog, cancellationToken);
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
            connection.Close();
        }
    }

    /// <summary>
    /// Opens a connection using whichever transport IPrinterConnectionModeProvider currently
    /// selects, without running anything against it or closing it - the caller owns its lifecycle
    /// from here. Used directly by PrinterConnectionSessionFactory so several steps can share one
    /// connection instead of each independently reconnecting; RunAsync above is built on top of this
    /// for the simpler one-shot callers.
    /// </summary>
    public static Task<Connection> OpenAsync(PrinterDevice device, IPrinterConnectionModeProvider connectionModeProvider, IAppLog appLog, CancellationToken cancellationToken)
    {
        if (connectionModeProvider.Mode == PrinterConnectionMode.Wifi && connectionModeProvider.WifiIpAddress is not null)
        {
            return OpenWifiAsync(connectionModeProvider.WifiIpAddress, cancellationToken);
        }

        return OpenBluetoothAsync(device.BluetoothMacAddress, appLog, cancellationToken);
    }

    private static async Task<Connection> OpenBluetoothAsync(string macAddress, IAppLog appLog, CancellationToken cancellationToken)
    {
        try
        {
            return await BluetoothConnectionRunner.OpenAsync(macAddress, appLog, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            appLog.Log($"{ex.Message} Trying Bluetooth Low Energy...", LogLevel.Warning);
            return await BleConnectionRunner.OpenAsync(macAddress, appLog, cancellationToken);
        }
    }

    private static Task<Connection> OpenWifiAsync(string ipAddress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            Connection connection = new TcpConnection(ipAddress, SgdPort);
            connection.Open();
            return connection;
        }, cancellationToken);
}
