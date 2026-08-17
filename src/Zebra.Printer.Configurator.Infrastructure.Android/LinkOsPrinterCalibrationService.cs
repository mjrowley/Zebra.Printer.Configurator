using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Triggers the printer's own SmartCal-equivalent media calibration via the "zpl.calibrate" SGD "do"
/// command (confirmed against Zebra's own ZPL Programming Guide - the SGD equivalent of the ZPL
/// "~JC" command/holding PAUSE + CANCEL on the printer for two seconds) - feeds and measures a few
/// labels to redetect media type/length and adjust gap/black-mark sensing levels for whatever's
/// currently loaded. A generous read timeout is used since this physically feeds media and takes a
/// few real seconds to complete, unlike most SGD.DO commands this app issues.
/// </summary>
public sealed class LinkOsPrinterCalibrationService(
    IBluetoothPermissionService bluetoothPermissionService,
    IPrinterConnectionModeProvider connectionModeProvider,
    IAppLog appLog) : IPrinterCalibrationService
{
    private const int CalibrationReadTimeoutMs = 15000;
    private const int CalibrationTimeToWaitForMoreDataMs = 500;

    public async Task CalibrateAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log("Calibrating media - the printer will feed a few labels...");
        await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            SGD.DO("zpl.calibrate", string.Empty, connection, CalibrationReadTimeoutMs, CalibrationTimeToWaitForMoreDataMs);
        }, appLog, cancellation: null, cancellationToken);

        appLog.Log("Media calibration complete.", LogLevel.Success);
    }
}
