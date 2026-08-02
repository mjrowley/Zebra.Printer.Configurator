using Microsoft.Maui.ApplicationModel;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Custom MAUI platform permission bundling BLUETOOTH_SCAN and BLUETOOTH_CONNECT, since neither is
/// one of MAUI's built-in cross-platform Permissions.
/// </summary>
public sealed class BluetoothConnectionPermissions : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (global::Android.Manifest.Permission.BluetoothScan, true),
        (global::Android.Manifest.Permission.BluetoothConnect, true),
    ];
}
