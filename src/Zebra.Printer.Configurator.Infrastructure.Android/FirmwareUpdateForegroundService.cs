using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Runs the firmware transfer as an Android foreground service (type "dataSync") rather than a plain
/// background Task tied to a Razor component's lifecycle - foreground services are specifically
/// exempted from Doze/App Standby's CPU *and* network restrictions while running, which a plain
/// PowerManager.WakeLock is not (confirmed on-device: a wake-lock-only transfer was still aborted
/// mid-write, "Software caused connection abort", after several minutes with the screen locked).
///
/// The actual SDK work is unchanged - this class is Android lifecycle/notification plumbing around
/// the existing IPrinterFirmwareUpdateService, resolved via AppServiceLocator since a
/// Service is instantiated directly by Android, not through the app's normal DI-constructed graph.
/// Progress/outcome are published to FirmwareUpdateStatusMonitor (observed by PrinterVersionAlert.razor
/// if the app is open) and to the notification (visible either way).
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class FirmwareUpdateForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ProgressChannelId = "firmware_update_progress";
    private const string AlertChannelId = "firmware_update_alert";

    private const string ExtraBluetoothMacAddress = "BluetoothMacAddress";
    private const string ExtraSerialNumber = "SerialNumber";
    private const string ExtraWifiMacAddress = "WifiMacAddress";
    private const string ExtraWifiIpAddress = "WifiIpAddress";
    private const string ExtraModelName = "ModelName";

    public static Intent CreateIntent(Context context, PrinterDevice device, FirmwareBundle bundle, string wifiIpAddress)
    {
        var intent = new Intent(context, typeof(FirmwareUpdateForegroundService));
        intent.PutExtra(ExtraBluetoothMacAddress, device.BluetoothMacAddress);
        intent.PutExtra(ExtraSerialNumber, device.SerialNumber);
        intent.PutExtra(ExtraWifiMacAddress, device.WifiMacAddress);
        intent.PutExtra(ExtraWifiIpAddress, wifiIpAddress);
        intent.PutExtra(ExtraModelName, bundle.ModelName);
        return intent;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var wifiIpAddress = intent?.GetStringExtra(ExtraWifiIpAddress);
        var modelName = intent?.GetStringExtra(ExtraModelName);
        var bundle = FirmwareBundleCatalog.All.FirstOrDefault(b => b.ModelName == modelName);
        var bluetoothMacAddress = intent?.GetStringExtra(ExtraBluetoothMacAddress);

        EnsureNotificationChannels();
        StartForeground(NotificationId, BuildProgressNotification("Starting firmware update...", null));

        if (string.IsNullOrWhiteSpace(wifiIpAddress) || bundle is null || string.IsNullOrWhiteSpace(bluetoothMacAddress))
        {
            // Nothing sensible to do without a target - stop immediately rather than sit in the
            // foreground state for no reason. Not expected in practice since FirmwareUpdateLauncher
            // always supplies these, but a Service can in principle be restarted by Android with a
            // stale/null Intent, so this is defensive rather than assumed impossible.
            StopForeground(StopForegroundFlags.Remove);
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        var device = new PrinterDevice
        {
            BluetoothMacAddress = bluetoothMacAddress,
            SerialNumber = intent!.GetStringExtra(ExtraSerialNumber),
            WifiMacAddress = intent.GetStringExtra(ExtraWifiMacAddress),
        };

        _ = RunUpdateAsync(device, bundle, wifiIpAddress, startId);

        return StartCommandResult.NotSticky;
    }

    private async Task RunUpdateAsync(PrinterDevice device, FirmwareBundle bundle, string wifiIpAddress, int startId)
    {
        var updateService = AppServiceLocator.GetRequiredService<IPrinterFirmwareUpdateService>();
        var statusMonitor = AppServiceLocator.GetRequiredService<FirmwareUpdateStatusMonitor>();
        var appLog = AppServiceLocator.GetRequiredService<IAppLog>();

        statusMonitor.SetRunning();

        var progress = new Progress<FirmwareUpdateProgress>(p =>
        {
            statusMonitor.SetProgress(p);
            UpdateProgressNotification(p);
        });

        try
        {
            await updateService.UpdateFirmwareAsync(device, bundle, wifiIpAddress, progress);
            statusMonitor.SetSucceeded();
            PostAlertNotification(
                "Firmware update complete",
                "Printer is back online with the new firmware - safe to continue.",
                LogLevel.Success);
        }
        catch (Exception ex)
        {
            appLog.Log($"Firmware update failed: {ex.Message}", LogLevel.Error);
            statusMonitor.SetFailed(ex.Message);
            PostAlertNotification("Firmware update failed", ex.Message, LogLevel.Error);
        }
        finally
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf(startId);
        }
    }

    private void EnsureNotificationChannels()
    {
        var notificationManager = (NotificationManager)Application.Context.GetSystemService(Context.NotificationService)!;

        // Low importance, no sound - this is an ongoing status, not something that should interrupt.
        var progressChannel = new NotificationChannel(ProgressChannelId, "Firmware update progress", NotificationImportance.Low)
        {
            Description = "Shows progress while a printer firmware update is being sent.",
        };
        progressChannel.SetSound(null, null);
        notificationManager.CreateNotificationChannel(progressChannel);

        // High importance with sound - this is the "come back and check on it" alert, for both
        // success and failure, since a failure is exactly when the user most needs to know not to
        // walk away expecting it to finish on its own.
        var alertChannel = new NotificationChannel(AlertChannelId, "Firmware update alerts", NotificationImportance.High)
        {
            Description = "Alerts when a printer firmware update finishes, successfully or not.",
        };
        notificationManager.CreateNotificationChannel(alertChannel);
    }

    private Notification BuildProgressNotification(string text, Core.Models.FirmwareUpdateProgress? progress)
    {
        var builder = new Notification.Builder(Application.Context, ProgressChannelId)
            .SetContentTitle("Updating printer firmware")
            .SetContentText(text)
            .SetSmallIcon(ResolveIconResourceId())
            .SetOngoing(true)
            .SetContentIntent(BuildContentIntent());

        if (progress is { Stage: FirmwareUpdateStage.Downloading, TotalBytes: > 0 } and { BytesWritten: { } written, TotalBytes: { } total })
        {
            builder.SetProgress(total, written, false);
        }
        else
        {
            builder.SetProgress(0, 0, true);
        }

        return builder.Build()!;
    }

    private void UpdateProgressNotification(Core.Models.FirmwareUpdateProgress progress)
    {
        var text = progress switch
        {
            { Stage: FirmwareUpdateStage.Downloading, BytesWritten: { } written, TotalBytes: { } total } and { TotalBytes: > 0 } =>
                $"Sending firmware to printer... {written * 100L / total}%",
            { Stage: FirmwareUpdateStage.Downloading } => "Sending firmware to printer...",
            { Stage: FirmwareUpdateStage.AwaitingReboot } => "Firmware sent - printer is flashing and rebooting...",
            { Stage: FirmwareUpdateStage.Complete } => "Printer is back online.",
            _ => "Starting firmware update...",
        };

        var notificationManager = (NotificationManager)Application.Context.GetSystemService(Context.NotificationService)!;
        notificationManager.Notify(NotificationId, BuildProgressNotification(text, progress));
    }

    private void PostAlertNotification(string title, string text, LogLevel level)
    {
        var notification = new Notification.Builder(Application.Context, AlertChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(text))
            .SetSmallIcon(ResolveIconResourceId())
            .SetAutoCancel(true)
            .SetContentIntent(BuildContentIntent())
            .Build();

        var notificationManager = (NotificationManager)Application.Context.GetSystemService(Context.NotificationService)!;
        // A different id from the ongoing progress notification (which StopForeground(Remove) already
        // removed) so this alert persists in the shade independently.
        notificationManager.Notify(NotificationId + 1, notification);
    }

    private static PendingIntent? BuildContentIntent()
    {
        var launchIntent = Application.Context.PackageManager?.GetLaunchIntentForPackage(Application.Context.PackageName!);
        return launchIntent is null
            ? null
            : PendingIntent.GetActivity(Application.Context, 0, launchIntent, PendingIntentFlags.Immutable);
    }

    // The App project's generated mipmap resource ID isn't reachable from this project (that would
    // require a reference back to App, which already references this project) - resolved by name
    // against the merged APK resources at runtime instead, with a built-in Android icon as a fallback
    // so a notification can still be posted (a 0 resource ID for SetSmallIcon throws) even if that
    // lookup somehow fails.
    private static int ResolveIconResourceId()
    {
        var id = Application.Context.Resources?.GetIdentifier("appicon", "mipmap", Application.Context.PackageName) ?? 0;
        return id != 0 ? id : global::Android.Resource.Drawable.StatSysDownload;
    }
}
