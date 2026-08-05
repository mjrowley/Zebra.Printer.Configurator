using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Comm;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Opens whichever connection the current IPrinterConnectionModeProvider selects - Bluetooth (via
/// BluetoothConnectionRunner, with its existing retry) or a plain TcpConnection to the printer's
/// WiFi IP - so every function that talks to the printer (apply configuration, restart, factory
/// reset, check configuration) automatically obeys the active transport instead of each one
/// hard-coding Bluetooth.
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

        return BluetoothConnectionRunner.RunAsync(device.BluetoothMacAddress, func, appLog, cancellationToken);
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
