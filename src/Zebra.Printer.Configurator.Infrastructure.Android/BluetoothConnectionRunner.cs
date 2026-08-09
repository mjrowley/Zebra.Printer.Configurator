using System.Diagnostics;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Workflow;
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
    // Widened from 3 attempts/2s (~4s of retrying) to 5/3s (~12s): confirmed on a real ZD621 that
    // pressing "Connect via WiFi" immediately after tap-pairing completes reliably fails all 3
    // original attempts (the Android Bluetooth stack is still settling from the just-finished bond),
    // while the identical read via "Check Configuration" succeeds when tried a bit later - the
    // previous budget didn't reliably cover that settling window.
    private const int ConnectionAttempts = 5;
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(3);

    public static Task RunAsync(string macAddress, Action<Connection> action, IAppLog appLog, PrinterOperationCancellation? cancellation, CancellationToken cancellationToken) =>
        RunAsync<object?>(macAddress, connection =>
        {
            action(connection);
            return null;
        }, appLog, cancellation, cancellationToken);

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
                    // The force-close above (triggered by PrinterOperationCancellation.Cancel())
                    // is what actually broke this blocking call - whatever raw I/O exception the
                    // SDK throws as a result is normalized here so callers only ever see a clean
                    // OperationCanceledException, not a confusing socket error.
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
    /// Opens a BluetoothConnection with the same retry behavior as RunAsync, but hands it back
    /// without running anything or closing it - the caller owns its lifecycle from here. Used by
    /// PrinterConnectionSessionFactory so several steps can share one connection instead of each
    /// independently reconnecting; RunAsync above is built on top of this for one-shot callers.
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
                    Connection connection = new BluetoothConnection(macAddress);
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
                appLog.Log($"Bluetooth connection attempt {attempt} of {ConnectionAttempts} failed ({JavaExceptionDescriber.Describe(ex)}). Retrying...", LogLevel.Warning);
                await Task.Delay(ConnectionRetryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                // Wrapped with the full Java cause chain rather than left as ex.Message alone - the
                // raw message here is frequently just "Exception of type 'Java.IO.IOException' was
                // thrown." (the Java side gave no message), which told a user nothing about what
                // actually failed. The original exception is preserved as InnerException.
                throw new InvalidOperationException(
                    $"Bluetooth connection to {macAddress} failed after {ConnectionAttempts} attempts: {JavaExceptionDescriber.Describe(ex)}", ex);
            }
        }

        // Unreachable: the final attempt (attempt == ConnectionAttempts) is always caught by the
        // unconditional catch above, so its exception always propagates out instead of falling
        // through to here.
        throw new UnreachableException();
    }
}
