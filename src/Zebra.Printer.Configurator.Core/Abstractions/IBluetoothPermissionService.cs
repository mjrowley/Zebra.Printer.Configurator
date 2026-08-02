namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Ensures Android's runtime Bluetooth permissions (BLUETOOTH_SCAN, BLUETOOTH_CONNECT - both
/// "dangerous" permissions since API 31, requiring an explicit user grant beyond just the
/// AndroidManifest.xml declaration) are in place before a BluetoothConnection is opened.
/// </summary>
public interface IBluetoothPermissionService
{
    Task<bool> EnsureGrantedAsync(CancellationToken cancellationToken = default);
}
