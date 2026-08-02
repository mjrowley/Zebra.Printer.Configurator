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
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(30);

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

        appLog.Log("Requesting Bluetooth pairing with printer...");

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
            return bonded;
        }
        finally
        {
            Application.Context.UnregisterReceiver(bondReceiver);
            Application.Context.UnregisterReceiver(pairingRequestReceiver);
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
            if (intent?.Action != BluetoothDevice.ActionPairingRequest || !MatchesTarget(intent, targetAddress))
            {
                return;
            }

            var device = GetDeviceExtra(intent);
            var variant = intent.GetIntExtra(BluetoothDevice.ExtraPairingVariant, -1);

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
        }
    }

    private static bool MatchesTarget(Intent intent, string targetAddress) =>
        string.Equals(GetDeviceExtra(intent)?.Address, targetAddress, StringComparison.OrdinalIgnoreCase);

    private static BluetoothDevice? GetDeviceExtra(Intent intent) =>
        intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice))) as BluetoothDevice;
}
