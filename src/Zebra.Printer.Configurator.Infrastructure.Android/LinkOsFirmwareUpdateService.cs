using Android.Content;
using Android.OS;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Sends a bundled firmware file to the printer over WiFi via the Link-OS SDK's own
/// FirmwareUpdaterLinkOs (reached through ZebraPrinterLinkOs.UpdateFirmwareUnconditionally),
/// following Zebra's own documented example exactly: a plain TcpConnection on
/// TcpConnection.DEFAULT_ZPL_TCP_PORT (the raw ZPL/firmware port - distinct from the SGD port 6101
/// used elsewhere in this app), wrapped with ZebraPrinterFactory.GetLinkOsPrinter.
///
/// Uses "Unconditionally" deliberately, not the plain UpdateFirmware overload - confirmed on-device
/// that the plain overload's own version check (which only compares the firmware string) silently
/// no-ops when the printer's firmware already matches the file, even when Link-OS itself is behind
/// (the two aren't always in lockstep - a printer can already have this exact firmware build while
/// still reporting an older Link-OS version). Since this method is only ever called after this app's
/// own PrinterVersionEvaluator has already decided an update is genuinely needed - a strictly more
/// nuanced check than the SDK's single-string comparison - that decision should be authoritative:
/// once we've decided to update, the transfer must actually happen, not be silently skipped by a
/// narrower internal check.
///
/// UpdateFirmwareUnconditionally is a long-running, synchronous, blocking SDK call (up to its own
/// 10-minute default timeout) that only returns once the printer has finished flashing and
/// reconnected, so it runs on a background thread via Task.Run, matching how every other blocking
/// SDK call in this app (BluetoothConnectionRunner, PrinterConnectionRunner) is wrapped.
///
/// Requires WiFi - the caller passes the printer's already-confirmed-reachable WiFi IP explicitly
/// (a 41MB Bluetooth Classic transfer would take several minutes, per the earlier decision to
/// require WiFi for updates).
///
/// Holds a partial WakeLock for the duration of the transfer - with no wake lock, the CPU can
/// suspend once the screen locks (timeout or power button), stalling or killing the background
/// thread mid-upload. A partial wake lock keeps the CPU running without also forcing the screen to
/// stay on, which is all a background network transfer actually needs. Given a timeout of its own
/// (longer than UpdateFirmwareUnconditionally's own 10-minute default) as a safety net in case the
/// finally block is somehow never reached, on top of the explicit Release() below.
/// </summary>
public sealed class LinkOsFirmwareUpdateService(IAppLog appLog) : IPrinterFirmwareUpdateService
{
    private static readonly TimeSpan WakeLockTimeout = TimeSpan.FromMinutes(20);

    public async Task UpdateFirmwareAsync(PrinterDevice device, FirmwareBundle bundle, string wifiIpAddress, IProgress<FirmwareUpdateProgress> progress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(wifiIpAddress))
        {
            throw new ArgumentException("A WiFi IP address is required to update firmware.", nameof(wifiIpAddress));
        }

        var ipAddress = wifiIpAddress;

        var powerManager = (PowerManager)Application.Context.GetSystemService(Context.PowerService)!;
        using var wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "ZebraPrinterConfigurator:FirmwareUpdate")!;
        wakeLock.Acquire((long)WakeLockTimeout.TotalMilliseconds);
        try
        {
            appLog.Log($"Preparing firmware file ({bundle.ExpectedFirmwareVersion})...");
            var firmwareFilePath = await BundledAssetProvider.GetLocalFilePathAsync(bundle.FirmwareAssetLogicalPath, cancellationToken);

            appLog.Log($"Sending firmware update to printer at {ipAddress}...");
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() =>
            {
                Connection connection = new TcpConnection(ipAddress, TcpConnection.DEFAULT_ZPL_TCP_PORT);
                connection.Open();
                try
                {
                    var printer = ZebraPrinterFactory.GetLinkOsPrinter(connection);
                    printer.UpdateFirmwareUnconditionally(firmwareFilePath, new FirmwareUpdateProgressHandler(progress, appLog));
                }
                finally
                {
                    connection.Close();
                }
            }, cancellationToken);

            appLog.Log("Firmware update complete.", LogLevel.Success);
        }
        finally
        {
            if (wakeLock.IsHeld)
            {
                wakeLock.Release();
            }
        }
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
