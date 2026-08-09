using Android.Bluetooth;
using Android.Content;
using Zebra.Printer.Configurator.Core.Abstractions;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Diagnostic only, not a permanent part of the pairing flow - confirms or rules out the theory
/// that the OS's own "Can't connect [printer]" system notification (which appears shortly after
/// pairing even though this app's own SPP/RFCOMM connections are demonstrably working - SGD
/// commands succeed while the dialog is still on screen, and Device Settings shows no actual
/// problem afterward) comes from Android's automatic attempt to connect standard Bluetooth
/// *profiles* (A2DP audio, HFP hands-free) to any newly-bonded device, entirely independent of
/// this app's own custom data connection to the printer. A label printer supports neither profile,
/// so Android's own attempt to connect them would fail and could plausibly be what the system
/// notification is actually reporting - unrelated to whether this app's own connection works.
///
/// Logs every profile connection-state change and raw ACL link connect/disconnect for the target
/// device, so an on-device test can show whether one of these fires (and fails) around when the
/// dialog appears. Dynamically registered (not manifest-declared) - these four actions are
/// confirmed exempt from Android's implicit-broadcast background restrictions (per Android's own
/// "Implicit broadcast exceptions" documentation), so either registration style would work for
/// them specifically, but dynamic keeps this consistent with BondStateReceiver and trivial to
/// remove once the theory is confirmed or ruled out.
/// </summary>
public sealed class BluetoothProfileDiagnostics(IAppLog appLog) : IBluetoothProfileDiagnostics, IDisposable
{
    private readonly Receiver _receiver = new(appLog);
    private bool _registered;

    public void Start(string targetAddress)
    {
        _receiver.TargetAddress = targetAddress;
        if (_registered)
        {
            return;
        }

        var filter = new IntentFilter();
        filter.AddAction(BluetoothA2dp.ActionConnectionStateChanged);
        filter.AddAction(BluetoothHeadset.ActionConnectionStateChanged);
        filter.AddAction(BluetoothDevice.ActionAclConnected);
        filter.AddAction(BluetoothDevice.ActionAclDisconnected);
        Application.Context.RegisterReceiver(_receiver, filter, ReceiverFlags.NotExported);
        _registered = true;
        appLog.Log($"[Profile diagnostics] Watching A2DP/HFP profile and ACL link events for {targetAddress}.");
    }

    public void Stop()
    {
        if (!_registered)
        {
            return;
        }

        Application.Context.UnregisterReceiver(_receiver);
        _registered = false;
    }

    public void Dispose() => Stop();

    private sealed class Receiver(IAppLog appLog) : BroadcastReceiver
    {
        public string? TargetAddress { get; set; }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action is null)
            {
                return;
            }

            var device = BluetoothPairingService.GetDeviceExtra(intent);
            if (!string.Equals(device?.Address, TargetAddress, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var description = intent.Action switch
            {
                BluetoothA2dp.ActionConnectionStateChanged => $"A2DP (audio) connection state: {DescribeStateChange(intent)}",
                BluetoothHeadset.ActionConnectionStateChanged => $"Headset (HFP) connection state: {DescribeStateChange(intent)}",
                BluetoothDevice.ActionAclConnected => "ACL link connected (baseband)",
                BluetoothDevice.ActionAclDisconnected => "ACL link disconnected (baseband)",
                _ => intent.Action,
            };

            appLog.Log($"[Profile diagnostics] {description}", LogLevel.Warning);
        }

        private static string DescribeStateChange(Intent intent)
        {
            // IBluetoothProfile, not the obsolete BluetoothProfile class - same extra-key strings,
            // but without the CS0618 "will be removed in a future release" warning.
            var previousState = intent.GetIntExtra(IBluetoothProfile.ExtraPreviousState, -1);
            var newState = intent.GetIntExtra(IBluetoothProfile.ExtraState, -1);
            return $"{DescribeProfileState(previousState)} -> {DescribeProfileState(newState)}";
        }

        // IBluetoothProfile.STATE_* aren't bound as named constants in this project's Mono.Android
        // binding, but are documented directly on the Java BluetoothProfile interface: 0 =
        // Disconnected, 1 = Connecting, 2 = Connected, 3 = Disconnecting.
        private static string DescribeProfileState(int state) => state switch
        {
            0 => "Disconnected",
            1 => "Connecting",
            2 => "Connected",
            3 => "Disconnecting",
            _ => $"Unknown({state})",
        };
    }
}
