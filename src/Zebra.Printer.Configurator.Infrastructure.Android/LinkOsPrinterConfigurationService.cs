using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Configuration;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;

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
    public async Task ApplyAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureBluetoothPermissionAsync(cancellationToken);

        await Task.Run(() =>
        {
            Connection connection = new BluetoothConnection(device.BluetoothMacAddress);
            connection.Open();
            try
            {
                foreach (var (key, value) in WlanConfigurationCommandBuilder.BuildSetCommands(configuration))
                {
                    SGD.SET(key, value, connection);
                }
            }
            finally
            {
                connection.Close();
            }
        }, cancellationToken);
    }

    public async Task RestartAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureBluetoothPermissionAsync(cancellationToken);

        await Task.Run(() =>
        {
            Connection connection = new BluetoothConnection(device.BluetoothMacAddress);
            connection.Open();
            try
            {
                SGD.DO("device.restart", string.Empty, connection);
            }
            finally
            {
                connection.Close();
            }
        }, cancellationToken);
    }

    private async Task EnsureBluetoothPermissionAsync(CancellationToken cancellationToken)
    {
        // Requested here, on the calling context, rather than inside Task.Run below - showing the
        // system permission dialog and awaiting the user's response needs the Activity, not a
        // background thread-pool thread.
        var granted = await bluetoothPermissionService.EnsureGrantedAsync(cancellationToken);
        if (!granted)
        {
            throw new InvalidOperationException(
                "Bluetooth permission is required to configure the printer. Please grant it and try again.");
        }
    }
}
