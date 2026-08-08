using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Sends a bundled firmware file to the printer over WiFi via the Link-OS SDK's own
/// FirmwareUpdaterLinkOs (reached through ZebraPrinterLinkOs.UpdateFirmware), following Zebra's own
/// documented example exactly: a plain TcpConnection on TcpConnection.DEFAULT_ZPL_TCP_PORT (the raw
/// ZPL/firmware port - distinct from the SGD port 6101 used elsewhere in this app), wrapped with
/// ZebraPrinterFactory.GetLinkOsPrinter. UpdateFirmware is a long-running, synchronous, blocking SDK
/// call (up to its own 10-minute default timeout) that only returns once the printer has finished
/// flashing and reconnected, so it runs on a background thread via Task.Run, matching how every other
/// blocking SDK call in this app (BluetoothConnectionRunner, PrinterConnectionRunner) is wrapped.
///
/// Requires WiFi - the caller passes the printer's already-confirmed-reachable WiFi IP explicitly
/// (a 41MB Bluetooth Classic transfer would take several minutes, per the earlier decision to
/// require WiFi for updates).
/// </summary>
public sealed class LinkOsFirmwareUpdateService(IAppLog appLog) : IPrinterFirmwareUpdateService
{
    public async Task UpdateFirmwareAsync(PrinterDevice device, FirmwareBundle bundle, string wifiIpAddress, IProgress<FirmwareUpdateProgress> progress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(wifiIpAddress))
        {
            throw new ArgumentException("A WiFi IP address is required to update firmware.", nameof(wifiIpAddress));
        }

        var ipAddress = wifiIpAddress;

        appLog.Log($"Preparing firmware file ({bundle.ExpectedFirmwareVersion})...");
        var firmwareFilePath = await FirmwareAssetProvider.GetLocalFilePathAsync(bundle.FirmwareAssetLogicalPath, cancellationToken);

        appLog.Log($"Sending firmware update to printer at {ipAddress}...");
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(() =>
        {
            Connection connection = new TcpConnection(ipAddress, TcpConnection.DEFAULT_ZPL_TCP_PORT);
            connection.Open();
            try
            {
                var printer = ZebraPrinterFactory.GetLinkOsPrinter(connection);
                printer.UpdateFirmware(firmwareFilePath, new FirmwareUpdateProgressHandler(progress, appLog));
            }
            finally
            {
                connection.Close();
            }
        }, cancellationToken);

        appLog.Log("Firmware update complete.", LogLevel.Success);
    }

    private sealed class FirmwareUpdateProgressHandler(IProgress<FirmwareUpdateProgress> progress, IAppLog appLog) : FirmwareUpdateHandler
    {
        public override void ProgressUpdate(int bytesWritten, int totalBytes) =>
            progress.Report(new FirmwareUpdateProgress
            {
                Stage = FirmwareUpdateStage.Downloading,
                BytesWritten = bytesWritten,
                TotalBytes = totalBytes,
            });

        public override void FirmwareDownloadComplete()
        {
            appLog.Log("Firmware download complete - printer is flashing and rebooting...");
            progress.Report(new FirmwareUpdateProgress { Stage = FirmwareUpdateStage.AwaitingReboot });
        }

        public override void PrinterOnline(ZebraPrinterLinkOs printer, string firmwareVersion)
        {
            appLog.Log($"Printer back online with firmware version {firmwareVersion}.", LogLevel.Success);
            progress.Report(new FirmwareUpdateProgress { Stage = FirmwareUpdateStage.Complete });
        }
    }
}
