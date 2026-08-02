using System.Diagnostics;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Sdk.Comm;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Opens a BluetoothConnection, runs an action against it, and closes it - with retry, since
/// Bluetooth Classic connections are commonly flaky. Shared between LinkOsPrinterConfigurationService
/// (apply/restart) and LinkOsConnectivityTestService (reading back WLAN settings for diagnostics
/// when the printer doesn't appear on WiFi after restart).
/// </summary>
internal static class BluetoothConnectionRunner
{
    private const int ConnectionAttempts = 3;
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(2);

    public static Task RunAsync(string macAddress, Action<Connection> action, IAppLog appLog, CancellationToken cancellationToken) =>
        RunAsync<object?>(macAddress, connection =>
        {
            action(connection);
            return null;
        }, appLog, cancellationToken);

    public static async Task<T> RunAsync<T>(string macAddress, Func<Connection, T> func, IAppLog appLog, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= ConnectionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await Task.Run(() =>
                {
                    Connection connection = new BluetoothConnection(macAddress);
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
            catch (Exception ex) when (attempt < ConnectionAttempts)
            {
                appLog.Log($"Bluetooth connection attempt {attempt} of {ConnectionAttempts} failed ({ex.Message}). Retrying...", LogLevel.Warning);
                await Task.Delay(ConnectionRetryDelay, cancellationToken);
            }
        }

        // Unreachable: the final attempt (attempt == ConnectionAttempts) has no retry filter, so
        // its exception always propagates out instead of falling through to here.
        throw new UnreachableException();
    }
}
