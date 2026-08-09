using Android.Bluetooth;
using Android.Content;
using Zebra.Printer.Configurator.Core.Abstractions;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Establishes an OS-level Bluetooth bond with the printer, matching Zebra's "Tap &amp; Pair" numeric
/// SSP flow (see zebra.com/.../tap-pair-instructions.html) but accepted automatically rather than
/// shown to the user for visual comparison - BluetoothPairingReceiver (a manifest-declared, always-
/// active BroadcastReceiver, not something this class registers itself) intercepts
/// ACTION_PAIRING_REQUEST and calls SetPairingConfirmation(true) directly. This class tracks
/// <see cref="CurrentPairingTargetAddress"/> so that receiver only ever acts on the device this call
/// itself just asked to bond with, never an unrelated Bluetooth device.
///
/// An earlier version of Bluetooth pairing in this app just called CreateBond() and let Android
/// handle confirmation on its own, which on-device testing showed sometimes fell back to a failing
/// legacy PIN negotiation (visible as an OS "PIN error" toast) instead of the printer's actual SSP
/// numeric-comparison method - driving the pairing-request broadcast explicitly avoids that.
///
/// Still under investigation as of this build - see BluetoothPairingReceiver's own doc comment for
/// the live dual-address theory and the diagnostic logging (device.Type, IdentityAddressWithType)
/// added here and in BondStateReceiver to confirm or rule it out on the next on-device test.
/// </summary>
public sealed class BluetoothPairingService(IBluetoothPermissionService bluetoothPermissionService, IAppLog appLog) : IBluetoothPairingService
{
    // Was temporarily widened to 60s while chasing a real bug that turned out to be something
    // else entirely: a second, unexpected BLE pairing negotiation (see PrinterConnectionRunner's
    // allowBleFallback) triggered by the automatic post-pair WiFi check, whose own "Can't connect"
    // failure was what actually produced the OS's "Can't connect" dialog and the illusion of the
    // Classic bond itself taking unusually long. Now that callers made shortly after a fresh bond
    // no longer fall back to BLE, the plain Classic bond this method waits on doesn't need a wider
    // timeout - back to 30s.
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UnbondTimeout = TimeSpan.FromSeconds(10);

    // Confirmed on-device: connecting again immediately after a freshly-completed bond can exhaust
    // enough of BluetoothConnectionRunner's own Classic retry budget to fall back to Bluetooth LE -
    // which, unlike Classic, has never been bonded for this device, so opening it silently triggers
    // a second, entirely separate OS pairing negotiation. Only applied after a bond this call itself
    // just completed - the "already paired" fast path above returns before any settling is needed.
    private static readonly TimeSpan PostBondSettlingDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The device address this call is currently trying to bond with, if any - checked by
    /// BluetoothPairingReceiver before it silently accepts a pairing request, so an always-active
    /// receiver never acts on some unrelated Bluetooth device's pairing request.
    /// </summary>
    internal static string? CurrentPairingTargetAddress { get; private set; }

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

        // Diagnostic: a dual-mode (BR/EDR + LE) printer using a separate BLE "identity address"
        // distinct from this Classic address is one live theory for why a second, unexplained
        // pairing negotiation keeps showing up - IdentityAddressWithType would reveal that address
        // directly if it exists. Only guaranteed present from API 36 - this app's min SDK is 33, so
        // it's explicitly guarded rather than risking a call the OS doesn't support at all on an
        // older device this app is otherwise fully compatible with.
        string? identityAddress = null;
        if (OperatingSystem.IsAndroidVersionAtLeast(36))
        {
            try
            {
                identityAddress = device.IdentityAddressWithType?.Address;
            }
            catch (Exception ex)
            {
                appLog.Log($"Could not read IdentityAddressWithType: {ex.Message}", LogLevel.Warning);
            }
        }

        appLog.Log($"Requesting Bluetooth pairing with printer ({device.Address}, type={device.Type}, identityAddress={identityAddress ?? "<none>"})...");

        var bondCompletion = new TaskCompletionSource<bool>();
        using var bondReceiver = new BondStateReceiver(device.Address!, bondCompletion, appLog);

        var stickyIntent = Application.Context.RegisterReceiver(bondReceiver, new IntentFilter(BluetoothDevice.ActionBondStateChanged), ReceiverFlags.NotExported);
        // Diagnostic: confirms RegisterReceiver actually returned rather than throwing or hanging -
        // if this line is missing from the log, registration itself never completed, which would
        // point upstream of BondStateReceiver even having a chance to run at all.
        appLog.Log($"BondStateReceiver registered for {device.Address} (RegisterReceiver returned {(stickyIntent is null ? "no sticky intent" : $"sticky intent action={stickyIntent.Action}")}).");

        CurrentPairingTargetAddress = device.Address;
        using var pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Diagnostic: an independent, broadcast-free way of detecting the bond completing - if this
        // detects Bonded while BondStateReceiver stays completely silent, that's conclusive proof
        // the ACTION_BOND_STATE_CHANGED broadcast itself never reaches this app's process, rather
        // than this app listening for the wrong thing.
        _ = PollBondStateAsync(device, bondCompletion, appLog, pollingCts.Token);
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
            pollingCts.Cancel();
            CurrentPairingTargetAddress = null;
            Application.Context.UnregisterReceiver(bondReceiver);
        }
    }

    // Diagnostic only - polls device.BondState directly, with no broadcast involved at all, purely
    // to determine whether BondStateReceiver's silence means the broadcast never arrives versus this
    // app listening for the wrong thing. Never throws out of this method (caught below) since it's
    // fire-and-forget from the caller's perspective.
    private static async Task PollBondStateAsync(BluetoothDevice device, TaskCompletionSource<bool> completion, IAppLog appLog, CancellationToken cancellationToken)
    {
        var lastState = device.BondState;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                var currentState = device.BondState;
                if (currentState != lastState)
                {
                    appLog.Log($"[Polling] device.BondState changed: {lastState} -> {currentState}.", LogLevel.Warning);
                    lastState = currentState;
                }

                if (currentState == Bond.Bonded)
                {
                    appLog.Log("[Polling] Detected Bonded via direct polling - independent of BondStateReceiver.", LogLevel.Success);
                    completion.TrySetResult(true);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected once the broadcast-based wait resolves first (this loop's token is cancelled
            // alongside it) or the caller's own cancellationToken fires.
        }
        catch (Exception ex)
        {
            appLog.Log($"[Polling] Unexpected error while polling device.BondState: {ex.Message}", LogLevel.Warning);
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
        using var bondReceiver = new BondStateReceiver(device.Address!, unbondCompletion, appLog);
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

    private sealed class BondStateReceiver(string targetAddress, TaskCompletionSource<bool> completionSource, IAppLog appLog) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothDevice.ActionBondStateChanged)
            {
                return;
            }

            var bondState = (Bond)intent.GetIntExtra(BluetoothDevice.ExtraBondState, (int)Bond.None);
            var transport = (BluetoothTransports)intent.GetIntExtra(BluetoothDevice.ExtraTransport, (int)BluetoothTransports.Auto);
            var deviceAddress = GetDeviceExtra(intent)?.Address;

            // Logged unconditionally, before the target-address filter below - a bond-state change
            // for a *different* address while waiting on targetAddress would be a direct sign of the
            // same dual-address theory BluetoothPairingReceiver's logging is checking for.
            appLog.Log($"BondStateReceiver.OnReceive: device={deviceAddress}, bondState={bondState}, transport={transport}, expected={targetAddress}");

            if (!MatchesTarget(intent, targetAddress))
            {
                return;
            }

            // Confirmed on-device (adb logcat during a repro): this printer is dual-mode and Android
            // bonds it over BLE and Classic/BR-EDR independently, each producing its own
            // ACTION_BOND_STATE_CHANGED sequence for the same address. This app only ever opens a
            // Classic BluetoothConnection (RFCOMM/SPP) - the LE bond is irrelevant to it and, being a
            // separate negotiation, can complete before or after the Classic one. Only Le is
            // explicitly excluded (rather than requiring == Bredr) so a broadcast that doesn't carry
            // this extra at all (defaults to Auto) - e.g. on an OS version or single-transport device
            // that behaves differently than this one - still completes pairing instead of hanging
            // until PairingTimeout.
            if (transport == BluetoothTransports.Le)
            {
                return;
            }

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

    private static bool MatchesTarget(Intent intent, string targetAddress) =>
        string.Equals(GetDeviceExtra(intent)?.Address, targetAddress, StringComparison.OrdinalIgnoreCase);

    // Internal (not private) - BluetoothPairingReceiver, a separate top-level class handling the
    // actual pairing-confirmation broadcast, reuses this rather than duplicating the extraction.
    internal static BluetoothDevice? GetDeviceExtra(Intent intent) =>
        intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice))) as BluetoothDevice;
}
