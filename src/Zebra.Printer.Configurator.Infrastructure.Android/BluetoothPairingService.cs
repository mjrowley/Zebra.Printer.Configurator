using Android.Bluetooth;
using Android.Content;
using Zebra.Printer.Configurator.Core.Abstractions;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Establishes an OS-level Bluetooth bond matching Zebra's own "Tap &amp; Pair" UX (see
/// zebra.com/.../tap-pair-instructions.html): the printer displays a numeric code, the phone must
/// show the same code, and the user confirms they match. Intercepts ACTION_PAIRING_REQUEST ahead of
/// Android's default Bluetooth Settings handling (via a high-priority ordered-broadcast receiver
/// plus AbortBroadcast) so the code is shown in this app's own UI instead of a system dialog.
///
/// An earlier version of Bluetooth pairing in this app just called CreateBond() and let Android
/// handle confirmation on its own, which on-device testing showed sometimes fell back to a failing
/// legacy PIN negotiation (visible as an OS "PIN error" toast) instead of the printer's actual SSP
/// numeric-comparison method - driving the pairing-request broadcast explicitly avoids that.
/// </summary>
public sealed class BluetoothPairingService(IBluetoothPermissionService bluetoothPermissionService, IAppLog appLog) : IBluetoothPairingService
{
    // Widened from 30s: confirmed on-device that the OS-level bond can still be genuinely in
    // progress past 30s (plausibly slower right after a factory reset, since the printer's
    // Bluetooth module has to redo the full SSP handshake from a clean state) - when the old
    // timeout fired first, this method gave up and unregistered its broadcast receivers, so the
    // later "Bonded" broadcast arrived with nothing listening: the user accepted the correct
    // passkey (confirmed by a matching label printing on the printer), the app logged "Bluetooth
    // pairing failed or timed out." and Android surfaced its own "Can't connect" dialog, yet a
    // retry immediately afterward reported "already paired" - the bond had actually completed in
    // the background, just after this method had already stopped watching for it.
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UnbondTimeout = TimeSpan.FromSeconds(10);

    // Confirmed on-device: connecting again immediately after a freshly-completed bond can exhaust
    // enough of BluetoothConnectionRunner's own Classic retry budget to fall back to Bluetooth LE -
    // which, unlike Classic, has never been bonded for this device, so opening it silently triggers
    // a second, entirely separate OS pairing negotiation (visible as a second "Pair again" system
    // dialog with a different code, which this app's own PairingRequestReceiver never sees since
    // it's only registered for the duration of this method). Only applied after a bond this call
    // itself just completed - the "already paired" fast path above returns before any settling is
    // needed.
    private static readonly TimeSpan PostBondSettlingDelay = TimeSpan.FromSeconds(2);

    public event EventHandler<PairingCodeRequestedEventArgs>? PairingCodeRequested;

    public async Task<bool> EnsurePairedAsync(string macAddress, CancellationToken cancellationToken = default)
    {
        var granted = await bluetoothPermissionService.EnsureGrantedAsync(cancellationToken);
        if (!granted)
        {
            appLog.Log("Bluetooth permission was not granted.", LogLevel.Error);
            return false;
        }

        var bluetoothManager = (BluetoothManager?)Application.Context.GetSystemService(Context.BluetoothService);
        var adapter = bluetoothManager?.Adapter
            ?? throw new InvalidOperationException("This device does not support Bluetooth.");

        var device = adapter.GetRemoteDevice(macAddress)
            ?? throw new InvalidOperationException($"'{macAddress}' is not a valid Bluetooth address.");
        if (device.BondState == Bond.Bonded)
        {
            appLog.Log("Printer is already paired.");
            return true;
        }

        appLog.Log($"Requesting Bluetooth pairing with printer ({device.Address})...");

        var bondCompletion = new TaskCompletionSource<bool>();
        using var bondReceiver = new BondStateReceiver(device.Address!, bondCompletion);
        using var pairingRequestReceiver = new PairingRequestReceiver(device.Address!, this);

        Application.Context.RegisterReceiver(bondReceiver, new IntentFilter(BluetoothDevice.ActionBondStateChanged), ReceiverFlags.NotExported);

        var pairingFilter = new IntentFilter(BluetoothDevice.ActionPairingRequest) { Priority = 999 };
        Application.Context.RegisterReceiver(pairingRequestReceiver, pairingFilter, ReceiverFlags.NotExported);

        try
        {
            if (!device.CreateBond())
            {
                appLog.Log("Could not start Bluetooth pairing.", LogLevel.Error);
                return false;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PairingTimeout);
            using var registration = timeoutCts.Token.Register(() => bondCompletion.TrySetResult(false));

            var bonded = await bondCompletion.Task;
            appLog.Log(
                bonded ? "Bluetooth pairing succeeded." : "Bluetooth pairing failed or timed out.",
                bonded ? LogLevel.Success : LogLevel.Error);

            if (bonded)
            {
                await Task.Delay(PostBondSettlingDelay, cancellationToken);
            }

            return bonded;
        }
        finally
        {
            Application.Context.UnregisterReceiver(bondReceiver);
            Application.Context.UnregisterReceiver(pairingRequestReceiver);
        }
    }

    public async Task RemoveBondAsync(string macAddress, CancellationToken cancellationToken = default)
    {
        var granted = await bluetoothPermissionService.EnsureGrantedAsync(cancellationToken);
        if (!granted)
        {
            appLog.Log("Bluetooth permission was not granted; cannot remove pairing.", LogLevel.Warning);
            return;
        }

        var bluetoothManager = (BluetoothManager?)Application.Context.GetSystemService(Context.BluetoothService);
        var adapter = bluetoothManager?.Adapter
            ?? throw new InvalidOperationException("This device does not support Bluetooth.");

        var device = adapter.GetRemoteDevice(macAddress)
            ?? throw new InvalidOperationException($"'{macAddress}' is not a valid Bluetooth address.");

        if (device.BondState != Bond.Bonded)
        {
            appLog.Log("No existing Bluetooth pairing to remove.");
            return;
        }

        // Waits for ACTION_BOND_STATE_CHANGED to actually confirm Bond.None, rather than firing
        // removeBond() and returning immediately - Android's unbonding is asynchronous, and starting
        // a fresh CreateBond() (as the next pairing attempt will) while the previous bond is still
        // mid-teardown is a known source of the Bluetooth stack falling back to a stale/legacy
        // pairing negotiation instead of a clean SSP handshake.
        var unbondCompletion = new TaskCompletionSource<bool>();
        using var bondReceiver = new BondStateReceiver(device.Address!, unbondCompletion);
        Application.Context.RegisterReceiver(bondReceiver, new IntentFilter(BluetoothDevice.ActionBondStateChanged), ReceiverFlags.NotExported);

        try
        {
            // BluetoothDevice.removeBond() is a hidden/SystemApi method, not part of the public
            // Android SDK (confirmed absent from Mono.Android's bound members via reflection against
            // the reference assembly), so normal apps can only reach it via Java reflection - there
            // is no public alternative for a non-privileged app to unpair a device.
            var removeBond = device.Class.GetMethod("removeBond", []);
            if (removeBond is null)
            {
                appLog.Log("Could not remove Bluetooth pairing: removeBond is not available on this device.", LogLevel.Warning);
                return;
            }

            removeBond.Invoke(device, []);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(UnbondTimeout);
            using var registration = timeoutCts.Token.Register(() => unbondCompletion.TrySetResult(false));

            await unbondCompletion.Task;
            var removed = device.BondState == Bond.None;
            appLog.Log(
                removed ? "Removed Bluetooth pairing with printer." : "Bluetooth pairing removal did not complete in time.",
                removed ? LogLevel.Success : LogLevel.Warning);
        }
        catch (Exception ex)
        {
            appLog.Log($"Could not remove Bluetooth pairing: {ex.Message}", LogLevel.Warning);
        }
        finally
        {
            Application.Context.UnregisterReceiver(bondReceiver);
        }
    }

    private void RaisePairingCodeRequested(PairingCodeRequestedEventArgs args) => PairingCodeRequested?.Invoke(this, args);

    private void Log(string message, LogLevel level = LogLevel.Info) => appLog.Log(message, level);

    private sealed class BondStateReceiver(string targetAddress, TaskCompletionSource<bool> completionSource) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothDevice.ActionBondStateChanged || !MatchesTarget(intent, targetAddress))
            {
                return;
            }

            var bondState = (Bond)intent.GetIntExtra(BluetoothDevice.ExtraBondState, (int)Bond.None);
            switch (bondState)
            {
                case Bond.Bonded:
                    completionSource.TrySetResult(true);
                    break;
                case Bond.None:
                    completionSource.TrySetResult(false);
                    break;
            }
        }
    }

    private sealed class PairingRequestReceiver(string targetAddress, BluetoothPairingService owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            // Logged unconditionally, before the filter below - if a pairing attempt fails with no
            // "pairing request" line at all in the log, this receiver either never ran (broadcast
            // priority/registration problem) or ran with an address that didn't match targetAddress
            // (visible here either way, rather than silently dropping out).
            var receivedAddress = intent is not null ? GetDeviceExtra(intent)?.Address : null;
            owner.Log($"PairingRequestReceiver.OnReceive: action={intent?.Action}, device={receivedAddress}, expected={targetAddress}");

            if (intent?.Action != BluetoothDevice.ActionPairingRequest || !MatchesTarget(intent, targetAddress))
            {
                return;
            }

            var device = GetDeviceExtra(intent);
            var variant = intent.GetIntExtra(BluetoothDevice.ExtraPairingVariant, -1);
            owner.Log($"Printer sent a pairing request (variant {variant}).");

            if (variant == BluetoothDevice.PairingVariantPasskeyConfirmation)
            {
                // Take over from Android's default Settings-app dialog so the code is shown in
                // this app's own UI, matching the printer's "compare the code on both devices" flow.
                InvokeAbortBroadcast();
                var passkey = intent.GetIntExtra(BluetoothDevice.ExtraPairingKey, -1);
                var args = new PairingCodeRequestedEventArgs(passkey.ToString("D6"));
                owner.Log($"Printer is requesting pairing confirmation. Code: {args.PairingCode}");
                owner.RaisePairingCodeRequested(args);

                args.Response.Task.ContinueWith(
                    t =>
                    {
                        var accepted = !t.IsFaulted && !t.IsCanceled && t.Result;
                        owner.Log(
                            accepted ? "User confirmed the pairing code." : "User rejected the pairing code.",
                            accepted ? LogLevel.Info : LogLevel.Warning);
                        device?.SetPairingConfirmation(accepted);
                    },
                    TaskScheduler.Default);
            }
            else
            {
                // Not the SSP numeric-comparison flow this app is built around - most likely the
                // printer fell back to legacy PIN pairing (e.g. after a factory reset reverted its
                // Bluetooth security settings). Left unhandled deliberately: Android's own Settings
                // dialog still gets this broadcast since it isn't aborted here, but logging it means
                // a "PIN error" on the printer now shows up here instead of vanishing silently.
                owner.Log(
                    $"Printer requested an unsupported pairing variant ({variant}) - expected SSP numeric comparison ({BluetoothDevice.PairingVariantPasskeyConfirmation}). This usually means the printer's Bluetooth security mode isn't set up for Tap & Pair.",
                    LogLevel.Warning);
            }
        }
    }

    private static bool MatchesTarget(Intent intent, string targetAddress) =>
        string.Equals(GetDeviceExtra(intent)?.Address, targetAddress, StringComparison.OrdinalIgnoreCase);

    private static BluetoothDevice? GetDeviceExtra(Intent intent) =>
        intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice))) as BluetoothDevice;
}
