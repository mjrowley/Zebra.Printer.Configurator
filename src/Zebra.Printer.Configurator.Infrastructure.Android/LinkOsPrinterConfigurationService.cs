using Android.Bluetooth;
using Android.Content;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Applies WLAN configuration and issues the restart command over Bluetooth - the printer isn't on
/// the target WiFi network yet at this point, so Bluetooth (paired via the MAC address read from
/// the NFC tag) is the only connection available. The SDK's Connection.Open/Close/SGD calls are
/// synchronous blocking I/O with no async overloads, so they're wrapped in Task.Run.
/// </summary>
public sealed class LinkOsPrinterConfigurationService(IBluetoothPermissionService bluetoothPermissionService)
    : IPrinterConfigurationService, IPrinterRestartService
{
    private const int ConnectionAttempts = 3;
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BondTimeout = TimeSpan.FromSeconds(15);

    public async Task ApplyAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await PrepareBluetoothAsync(device.BluetoothMacAddress, cancellationToken);

        await WithBluetoothConnectionAsync(device.BluetoothMacAddress, connection =>
        {
            foreach (var (key, value) in WlanConfigurationCommandBuilder.BuildSetCommands(configuration))
            {
                SGD.SET(key, value, connection);
            }
        }, cancellationToken);
    }

    public async Task RestartAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await PrepareBluetoothAsync(device.BluetoothMacAddress, cancellationToken);

        await WithBluetoothConnectionAsync(device.BluetoothMacAddress, connection =>
        {
            SGD.DO("device.restart", string.Empty, connection);
        }, cancellationToken);
    }

    private async Task PrepareBluetoothAsync(string macAddress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Requested/awaited here, on the calling context, rather than inside Task.Run below -
        // showing the system permission/pairing dialogs needs the Activity, not a background
        // thread-pool thread.
        var granted = await bluetoothPermissionService.EnsureGrantedAsync(cancellationToken);
        if (!granted)
        {
            throw new InvalidOperationException(
                "Bluetooth permission is required to configure the printer. Please grant it and try again.");
        }

        await EnsureBondedAsync(macAddress, cancellationToken);
    }

    /// <summary>
    /// The app's whole point is that the user shouldn't have to separately pair the printer via
    /// Android's Bluetooth settings before tapping NFC, so this attempts the OS-level bond
    /// programmatically as a best effort. It deliberately doesn't throw on failure/timeout: many
    /// Zebra SPP printers accept RFCOMM connections without a prior OS-level bond at all, so a
    /// bonding attempt that doesn't succeed (or that the printer doesn't respond to the way a
    /// standard Bluetooth peripheral would) shouldn't block the actual connection attempt -
    /// WithBluetoothConnectionAsync's retry loop below is the real, definitive test of whether the
    /// printer is reachable.
    /// </summary>
    private static async Task EnsureBondedAsync(string macAddress, CancellationToken cancellationToken)
    {
        var bluetoothManager = (BluetoothManager?)Application.Context.GetSystemService(Context.BluetoothService);
        var adapter = bluetoothManager?.Adapter
            ?? throw new InvalidOperationException("This device does not support Bluetooth.");

        var device = adapter.GetRemoteDevice(macAddress)
            ?? throw new InvalidOperationException($"'{macAddress}' is not a valid Bluetooth address.");
        if (device.BondState == Bond.Bonded)
        {
            return;
        }

        await WaitForBondAsync(device, cancellationToken);
    }

    private static async Task<bool> WaitForBondAsync(BluetoothDevice device, CancellationToken cancellationToken)
    {
        var completionSource = new TaskCompletionSource<bool>();
        using var receiver = new BondStateReceiver(device.Address!, completionSource);

        Application.Context.RegisterReceiver(receiver, new IntentFilter(BluetoothDevice.ActionBondStateChanged), ReceiverFlags.NotExported);
        try
        {
            if (!device.CreateBond())
            {
                return false;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(BondTimeout);
            using var registration = timeoutCts.Token.Register(() => completionSource.TrySetResult(false));

            return await completionSource.Task;
        }
        finally
        {
            Application.Context.UnregisterReceiver(receiver);
        }
    }

    private async Task WithBluetoothConnectionAsync(string macAddress, Action<Connection> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= ConnectionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Task.Run(() =>
                {
                    Connection connection = new BluetoothConnection(macAddress);
                    connection.Open();
                    try
                    {
                        action(connection);
                    }
                    finally
                    {
                        connection.Close();
                    }
                }, cancellationToken);
                return;
            }
            catch (Exception) when (attempt < ConnectionAttempts)
            {
                // Bluetooth Classic connections are commonly flaky even to a correctly bonded
                // device (interference, timing races just after bonding) - a short retry resolves
                // most transient "read failed"-style errors.
                await Task.Delay(ConnectionRetryDelay, cancellationToken);
            }
        }
    }

    private sealed class BondStateReceiver(string targetAddress, TaskCompletionSource<bool> completionSource) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothDevice.ActionBondStateChanged)
            {
                return;
            }

            var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice))) as BluetoothDevice;
            if (device is null || !string.Equals(device.Address, targetAddress, StringComparison.OrdinalIgnoreCase))
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
}
