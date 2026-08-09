using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Opens one printer connection for a caller to share across several steps (applying WLAN config,
/// enabling PDF Direct, restarting) instead of each independently reconnecting - see
/// PrinterConnectionSession's doc comment for the rest of how the shared connection is used.
///
/// Registers the opened connection with PrinterOperationCancellation once, for the whole session's
/// lifetime, rather than per step - the header's Cancel button force-closing it still interrupts
/// whichever step is currently blocked on it, exactly as before, just with one registration instead
/// of several.
/// </summary>
public sealed class PrinterConnectionSessionFactory(
    IBluetoothPermissionService bluetoothPermissionService,
    IPrinterConnectionModeProvider connectionModeProvider,
    IAppLog appLog,
    PrinterOperationCancellation cancellation) : IPrinterConnectionSessionFactory
{
    public async Task<IPrinterConnectionSession> OpenAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log($"Connecting to printer over {connectionModeProvider.Mode}...");
        var connection = await PrinterConnectionRunner.OpenAsync(device, connectionModeProvider, appLog, cancellationToken);
        var unregister = cancellation.TrackActiveConnection(connection.Close);
        return new PrinterConnectionSession(connection, unregister);
    }
}
