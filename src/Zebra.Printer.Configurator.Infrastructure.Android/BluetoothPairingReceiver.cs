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
///
/// Still under investigation as of this build: silent acceptance alone hasn't eliminated a second
/// pairing dialog with a different code. One live theory - a dual-mode (BR/EDR + LE) printer using a
/// separate BLE "identity address" distinct from its Classic address, which this receiver's
/// address-match filter would reject as "unrelated" even though it's the same physical printer - is
/// why every pairing request logs its full detail unconditionally, before any filtering, rather than
/// only once matched.
/// </summary>
[BroadcastReceiver(Exported = false, Label = "Bluetooth Pairing Receiver")]
[IntentFilter(new[] { BluetoothDevice.ActionPairingRequest })]
public sealed class BluetoothPairingReceiver : BroadcastReceiver
{
    // Not bound as a named constant in this project's Mono.Android binding (BluetoothDevice.CreateBond(int)
    // isn't bound either), but documented directly on the Java BluetoothDevice.EXTRA_TRANSPORT since
    // Android 8 (API 26): 0 = TRANSPORT_AUTO, 1 = TRANSPORT_BREDR (Classic), 2 = TRANSPORT_LE.
    private const string ExtraTransport = "android.bluetooth.device.extra.TRANSPORT";

    public override void OnReceive(Context? context, Intent? intent)
    {
        var appLog = AppServiceLocator.Services?.GetService(typeof(IAppLog)) as IAppLog;
        var device = intent is not null ? BluetoothPairingService.GetDeviceExtra(intent) : null;
        var transport = intent?.GetIntExtra(ExtraTransport, -1) ?? -1;
        var variant = intent?.GetIntExtra(BluetoothDevice.ExtraPairingVariant, -1) ?? -1;

        // Logged unconditionally, before any filtering at all - if a pairing attempt fails with no
        // trace of this line in the Activity Log, this receiver either never ran (a manifest
        // registration/broadcast-delivery problem) or Android delivered something other than
        // ACTION_PAIRING_REQUEST. Includes IdentityAddressWithType, which is null/absent for a
        // Classic-only device but reveals a separate BLE identity address for a dual-mode one -
        // directly confirms or rules out the "two different addresses for one physical printer" theory.
        appLog?.Log(
            $"BluetoothPairingReceiver.OnReceive: action={intent?.Action}, device={device?.Address}, "
            + $"type={SafeGet(() => device?.Type.ToString())}, bondState={SafeGet(() => device?.BondState.ToString())}, "
            + $"identityAddress={GetIdentityAddress(device)}, "
            + $"transport={transport} (0=Auto,1=Classic,2=LE), variant={variant}, "
            + $"currentTarget={BluetoothPairingService.CurrentPairingTargetAddress}");

        if (intent?.Action != BluetoothDevice.ActionPairingRequest)
        {
            return;
        }

        if (!string.Equals(device?.Address, BluetoothPairingService.CurrentPairingTargetAddress, StringComparison.OrdinalIgnoreCase))
        {
            appLog?.Log($"Ignoring Bluetooth pairing request from {device?.Address} - not the printer this app is currently pairing with.", LogLevel.Warning);
            return;
        }

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

    private static string SafeGet(Func<string?> getter)
    {
        try
        {
            return getter() ?? "<null>";
        }
        catch (Exception ex)
        {
            return $"<unavailable: {ex.Message}>";
        }
    }

    // IdentityAddressWithType is only guaranteed present from API 36 - this app's min SDK is 33, so
    // it's explicitly guarded (not just try/caught) rather than risking a call the OS doesn't
    // support at all on an older device this app is otherwise fully compatible with.
    private static string GetIdentityAddress(BluetoothDevice? device)
    {
        if (device is null || !OperatingSystem.IsAndroidVersionAtLeast(36))
        {
            return "<unavailable below Android 15 (API 36)>";
        }

        try
        {
            return device.IdentityAddressWithType?.Address ?? "<null>";
        }
        catch (Exception ex)
        {
            return $"<unavailable: {ex.Message}>";
        }
    }
}
