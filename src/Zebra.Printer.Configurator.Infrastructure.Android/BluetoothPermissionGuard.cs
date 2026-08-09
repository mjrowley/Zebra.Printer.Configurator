using Zebra.Printer.Configurator.Core.Abstractions;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Ensures Bluetooth permission is granted before opening a Bluetooth connection - a no-op when the
/// active transport is WiFi. Shared between PrinterConnectionSessionFactory and the direct-connection
/// methods LinkOsPrinterConfigurationService still has (ResetToFactoryDefaultsAsync/
/// ReadConfigurationAsync), which don't go through a shared session.
/// </summary>
internal static class BluetoothPermissionGuard
{
    public static async Task EnsureGrantedAsync(
        IBluetoothPermissionService bluetoothPermissionService,
        IPrinterConnectionModeProvider connectionModeProvider,
        IAppLog appLog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No Bluetooth permission needed at all when the active transport is WiFi - nothing here
        // touches the Bluetooth stack in that case.
        if (connectionModeProvider.Mode != PrinterConnectionMode.Bluetooth)
        {
            return;
        }

        // Requested/awaited here, on the calling context, rather than inside Task.Run - showing the
        // system permission dialog needs the Activity, not a background thread-pool thread.
        var granted = await bluetoothPermissionService.EnsureGrantedAsync(cancellationToken);
        if (!granted)
        {
            appLog.Log("Bluetooth permission is required to configure the printer.", LogLevel.Error);
            throw new InvalidOperationException(
                "Bluetooth permission is required to configure the printer. Please grant it and try again.");
        }
    }
}
