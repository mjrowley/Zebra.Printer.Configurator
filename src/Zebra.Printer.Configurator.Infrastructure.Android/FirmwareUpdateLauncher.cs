using Microsoft.Maui.ApplicationModel;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Starts FirmwareUpdateForegroundService with the given update's parameters, mirroring
/// BluetoothPermissionService's pattern for requesting a runtime permission before the operation
/// that needs it - POST_NOTIFICATIONS here (required on Android 13+ to show any notification at
/// all), using MAUI's built-in Permissions.PostNotifications rather than a custom permission class
/// like Bluetooth's, since this one is already a first-class MAUI permission.
/// </summary>
public sealed class FirmwareUpdateLauncher : IFirmwareUpdateLauncher
{
    public async Task StartAsync(PrinterDevice device, FirmwareBundle bundle, string wifiIpAddress, CancellationToken cancellationToken = default)
    {
        // Best-effort: if denied, the foreground service still runs and the transfer still
        // completes (StartForeground doesn't require the notification to actually be visible to
        // succeed) - the user just won't see the progress/completion notifications, which is exactly
        // the same trade-off as declining any other optional permission in this app.
        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            await Permissions.RequestAsync<Permissions.PostNotifications>();
        }

        var intent = FirmwareUpdateForegroundService.CreateIntent(Application.Context, device, bundle, wifiIpAddress);
        Application.Context.StartForegroundService(intent);
    }
}
