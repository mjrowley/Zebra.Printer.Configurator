using System.Diagnostics;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Workflow;
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

    public static async Task<T> RunAsync<T>(string macAddress, Func<Connection, T> func, IAppLog appLog, PrinterOperationCancellation? cancellation, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(macAddress, appLog, cancellationToken);
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
            // See PrinterConnectionRunner.RunAsync's own comment - a plain synchronous Close() here
            // runs on the UI thread's SynchronizationContext (confirmed on-device: it stalled the
            // main thread for several seconds, per Android's own Choreographer/HWUI frame-skip logs),
            // not a background thread, despite Open()/func() above both running on one.
            await Task.Run(connection.Close);
        }
    }

    /// <summary>
    /// Opens a BluetoothLeConnection with the same retry behavior as RunAsync, but hands it back
    /// without running anything or closing it - see BluetoothConnectionRunner.OpenAsync for why.
    /// </summary>
    public static async Task<Connection> OpenAsync(string macAddress, IAppLog appLog, CancellationToken cancellationToken)
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
                    return connection;
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
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
