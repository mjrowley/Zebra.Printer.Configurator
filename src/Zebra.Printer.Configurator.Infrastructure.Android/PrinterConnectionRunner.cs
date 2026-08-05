using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
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

    public static Task RunAsync(PrinterDevice device, IPrinterConnectionModeProvider connectionModeProvider, Action<Connection> action, IAppLog appLog, CancellationToken cancellationToken) =>
        RunAsync<object?>(device, connectionModeProvider, connection =>
        {
            action(connection);
            return null;
        }, appLog, cancellationToken);

    public static Task<T> RunAsync<T>(PrinterDevice device, IPrinterConnectionModeProvider connectionModeProvider, Func<Connection, T> func, IAppLog appLog, CancellationToken cancellationToken)
    {
        if (connectionModeProvider.Mode == PrinterConnectionMode.Wifi && connectionModeProvider.WifiIpAddress is not null)
        {
            return RunOverWifiAsync(connectionModeProvider.WifiIpAddress, func, cancellationToken);
        }

        return RunOverBluetoothAsync(device.BluetoothMacAddress, func, appLog, cancellationToken);
    }

    private static async Task<T> RunOverBluetoothAsync<T>(string macAddress, Func<Connection, T> func, IAppLog appLog, CancellationToken cancellationToken)
    {
        try
        {
            return await BluetoothConnectionRunner.RunAsync(macAddress, func, appLog, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            appLog.Log($"{ex.Message} Trying Bluetooth Low Energy...", LogLevel.Warning);
            return await BleConnectionRunner.RunAsync(macAddress, func, appLog, cancellationToken);
        }
    }

    private static Task<T> RunOverWifiAsync<T>(string ipAddress, Func<Connection, T> func, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            Connection connection = new TcpConnection(ipAddress, SgdPort);
            connection.Open();
            try
            {
                return func(connection);
            }
            finally
            {
                connection.Close();
            }
        }, cancellationToken);
}
