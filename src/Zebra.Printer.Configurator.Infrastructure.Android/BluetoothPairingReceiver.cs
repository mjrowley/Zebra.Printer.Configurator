using Android.App;
using Android.Bluetooth;
using Android.Content;
using Zebra.Printer.Configurator.Core.Abstractions;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Silently accepts the printer's SSP passkey-confirmation pairing request, replacing the interactive
/// "confirm this code matches the printer" step this app used to show.
///
/// Manifest-declared ([BroadcastReceiver]/[IntentFilter]) rather than dynamically registered for just
/// the duration of one BluetoothPairingService.EnsurePairedAsync call, the way this app used to do it.
/// Bluetooth pairing/bond broadcasts are exempt from Android's implicit-broadcast background
/// restrictions, so a manifest receiver is always listening regardless of timing - closing a real gap
/// the old dynamically-registered receiver had (confirmed on-device: a pairing request sometimes
/// arrived with nothing registered to intercept it at all, no diagnostic log line and all, falling
/// straight through to Android's own default pairing dialog).
///
/// Only ever acts on the device BluetoothPairingService.CurrentPairingTargetAddress names - being
/// always-active means this receiver could otherwise see ACTION_PAIRING_REQUEST broadcasts for
/// completely unrelated Bluetooth devices (e.g. the user pairing headphones via Android's own Settings
/// app) and must never silently accept those.
/// </summary>
[BroadcastReceiver(Exported = false, Label = "Bluetooth Pairing Receiver")]
[IntentFilter(new[] { BluetoothDevice.ActionPairingRequest })]
public sealed class BluetoothPairingReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != BluetoothDevice.ActionPairingRequest)
        {
            return;
        }

        var appLog = AppServiceLocator.Services?.GetService(typeof(IAppLog)) as IAppLog;
        var device = BluetoothPairingService.GetDeviceExtra(intent);

        if (!string.Equals(device?.Address, BluetoothPairingService.CurrentPairingTargetAddress, StringComparison.OrdinalIgnoreCase))
        {
            appLog?.Log($"Ignoring Bluetooth pairing request from {device?.Address} - not the printer this app is currently pairing with.", LogLevel.Warning);
            return;
        }

        var variant = intent.GetIntExtra(BluetoothDevice.ExtraPairingVariant, -1);
        appLog?.Log($"BluetoothPairingReceiver.OnReceive: device={device?.Address}, variant={variant}");

        if (variant != BluetoothDevice.PairingVariantPasskeyConfirmation)
        {
            // Not the SSP numeric-comparison flow this app is built around - most likely the printer
            // fell back to legacy PIN pairing (e.g. after a factory reset reverted its Bluetooth
            // security settings). Left unhandled deliberately: Android's own Settings dialog still
            // gets this broadcast since it isn't aborted here, but logging it means a "PIN error" on
            // the printer now shows up here instead of vanishing silently.
            appLog?.Log(
                $"Printer requested an unsupported pairing variant ({variant}) - expected SSP numeric comparison ({BluetoothDevice.PairingVariantPasskeyConfirmation}). This usually means the printer's Bluetooth security mode isn't set up for Tap & Pair.",
                LogLevel.Warning);
            return;
        }

        // Take over from Android's default Settings-app dialog and accept immediately, without
        // asking the user to visually compare codes - see this class's own doc comment for why.
        InvokeAbortBroadcast();
        var passkey = intent.GetIntExtra(BluetoothDevice.ExtraPairingKey, -1);
        appLog?.Log($"Printer requested pairing confirmation (code {passkey:D6}) - accepted automatically.", LogLevel.Success);
        device?.SetPairingConfirmation(true);
    }
}
