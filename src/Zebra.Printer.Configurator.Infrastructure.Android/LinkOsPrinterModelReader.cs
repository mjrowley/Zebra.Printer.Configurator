using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Reads the printer's own reported model name ("device.product_name" - the same SGD key
/// LinkOsPrinterVersionCheckService uses to identify a bundle) over whichever transport is
/// currently active. A single, uncached, un-retried read - unlike
/// LinkOsPrinterVersionCheckService.GetProductNameWithRetry, this only ever runs right after
/// Pairing.razor's stable Ready state (Configure.razor's initial load), not immediately after a
/// firmware-update reboot, so there's no equivalent race to retry around.
/// </summary>
public sealed class LinkOsPrinterModelReader(
    IBluetoothPermissionService bluetoothPermissionService,
    IPrinterConnectionModeProvider connectionModeProvider,
    IAppLog appLog) : IPrinterModelReader
{
    public async Task<string?> ReadModelNameAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log("Reading printer model name...");
        var productName = await PrinterConnectionRunner.RunAsync(device, connectionModeProvider,
            connection => SGD.GET("device.product_name", connection), appLog, cancellation: null, cancellationToken);

        appLog.Log(
            string.IsNullOrWhiteSpace(productName)
                ? "Printer did not report a model name."
                : $"Printer model: {productName}",
            string.IsNullOrWhiteSpace(productName) ? LogLevel.Warning : LogLevel.Info);

        return productName;
    }
}
