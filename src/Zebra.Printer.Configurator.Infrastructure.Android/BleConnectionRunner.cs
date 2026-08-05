using System.Diagnostics;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Comm.Btle;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Opens a BluetoothLeConnection, runs an action against it, and closes it - the last-resort
/// fallback PrinterConnectionRunner reaches for once Bluetooth Classic (BluetoothConnectionRunner)
/// has exhausted its own retries, mirroring Zebra's own Printer Setup Utility's documented
/// connection search order (network, then Bluetooth Classic, then Bluetooth LE). Deliberately does
/// not attempt any OS-level bond first - BluetoothLeConnection's GATT-based link doesn't require one.
/// </summary>
internal static class BleConnectionRunner
{
    // A shorter budget than Bluetooth Classic's - this only runs after Classic has already spent its
    // own retry budget failing, so keeping this tight limits how long a truly unreachable printer
    // takes to finally report failure.
    private const int ConnectionAttempts = 2;
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(2);

    public static async Task<T> RunAsync<T>(string macAddress, Func<Connection, T> func, IAppLog appLog, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= ConnectionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await Task.Run(() =>
                {
                    Connection connection = new BluetoothLeConnection(macAddress);
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
                appLog.Log($"Bluetooth LE connection attempt {attempt} of {ConnectionAttempts} failed ({JavaExceptionDescriber.Describe(ex)}). Retrying...", LogLevel.Warning);
                await Task.Delay(ConnectionRetryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Bluetooth LE connection to {macAddress} failed after {ConnectionAttempts} attempts: {JavaExceptionDescriber.Describe(ex)}", ex);
            }
        }

        // Unreachable: the final attempt (attempt == ConnectionAttempts) is always caught by the
        // unconditional catch above, so its exception always propagates out instead of falling
        // through to here.
        throw new UnreachableException();
    }
}
