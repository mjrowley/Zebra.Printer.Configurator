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
/// wrapped with ZebraPrinterFactory.GetLinkOsPrinter.
///
/// Uses port 6101 (LinkOsPort below), not TcpConnection.DEFAULT_ZPL_TCP_PORT (9100) - 9100 was
/// observed on-device throttling a 41MB firmware transfer to ~10 minutes, and (confirmed separately
/// via direct on-device port testing, 2026-08-19) is the port general SGD/status traffic actually
/// needs - 6101 is reserved specifically for large file transfers, matching
/// PrinterConnectionRunner.FileTransferSgdPort (used explicitly by the bag tag template push, the
/// other large binary payload in this app), proven fast and reliable on this port.
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
/// UpdateFirmwareUnconditionally is a long-running, synchronous, blocking SDK call that only returns
/// once the printer has finished flashing and reconnected, so it runs on a background thread via
/// Task.Run, matching how every other blocking SDK call in this app (BluetoothConnectionRunner,
/// PrinterConnectionRunner) is wrapped. Called with the explicit-timeout overload
/// (FirmwareUpdateTimeout below) rather than relying on the SDK's own undocumented-for-this-version
/// default, since the real-world worst case observed on-device (~10 minute transfer alone) leaves
/// little confidence in an unstated default.
///
/// Retries the whole transfer from scratch (not a byte-offset resume - the SDK has no such API, and
/// on-device testing confirmed the printer safely discards an incomplete/corrupt transfer rather
/// than committing it) once on failure, to smooth over a transient network blip on a link that spends
/// several minutes moving 41MB.
///
/// Requires WiFi - the caller passes the printer's already-confirmed-reachable WiFi IP explicitly
/// (a 41MB Bluetooth Classic transfer would take several minutes, per the earlier decision to
/// require WiFi for updates).
///
/// Holds a partial WakeLock for the duration of the transfer - with no wake lock, the CPU can
/// suspend once the screen locks (timeout or power button), stalling or killing the background
/// thread mid-upload. A partial wake lock keeps the CPU running without also forcing the screen to
/// stay on, which is all a background network transfer actually needs. Given a timeout of its own
/// (longer than FirmwareUpdateTimeout, covering both retry attempts) as a safety net in case the
/// finally block is somehow never reached, on top of the explicit Release() below.
/// </summary>
public sealed class LinkOsFirmwareUpdateService(IAppLog appLog) : IPrinterFirmwareUpdateService
{
    private const int LinkOsPort = 6101;
    private const int WriteChunkSizeBytes = 65536;
    private const int MaxAttempts = 2;

    private static readonly TimeSpan FirmwareUpdateTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan WakeLockTimeout = TimeSpan.FromMinutes(45);

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

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                appLog.Log(attempt == 1
                    ? $"Sending firmware update to printer at {ipAddress}..."
                    : $"Retrying firmware update (attempt {attempt} of {MaxAttempts})...");

                try
                {
                    await Task.Run(() => SendFirmwareOnce(ipAddress, firmwareFilePath, bundle, progress), cancellationToken);
                    appLog.Log("Firmware update complete.", LogLevel.Success);
                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts)
                {
                    appLog.Log($"Firmware update attempt {attempt} of {MaxAttempts} failed ({ex.Message}) - retrying from the start...", LogLevel.Warning);
                }
            }
        }
        finally
        {
            if (wakeLock.IsHeld)
            {
                wakeLock.Release();
            }
        }
    }

    private void SendFirmwareOnce(string ipAddress, string firmwareFilePath, FirmwareBundle bundle, IProgress<FirmwareUpdateProgress> progress)
    {
        var connection = new TcpConnection(ipAddress, LinkOsPort);
        connection.MaxDataToWrite = WriteChunkSizeBytes;
        connection.Open();
        try
        {
            var printer = ZebraPrinterFactory.GetLinkOsPrinter(connection);
            var handler = new FirmwareUpdateProgressHandler(progress, appLog, bundle.ExpectedFirmwareVersion);
            printer.UpdateFirmwareUnconditionally(firmwareFilePath, (long)FirmwareUpdateTimeout.TotalMilliseconds, handler);
            handler.EnsureExpectedVersionInstalled();
        }
        finally
        {
            try
            {
                connection.Close();
            }
            catch (ConnectionException)
            {
                // Expected: the printer intentionally drops the connection immediately after
                // FirmwareDownloadComplete() fires, to reboot and flash. By this point
                // UpdateFirmwareUnconditionally has already either succeeded or thrown its own real
                // exception, so the socket already being gone during this best-effort cleanup isn't
                // a new failure and must not mask whichever of those already happened.
            }
        }
    }

    private sealed class FirmwareUpdateProgressHandler(IProgress<FirmwareUpdateProgress> progress, IAppLog appLog, string expectedFirmwareVersion) : FirmwareUpdateHandler
    {
        private string? _reportedFirmwareVersion;

        public override void ProgressUpdate(int bytesWritten, int totalBytes) =>
            progress.Report(new FirmwareUpdateProgress
            {
                Stage = FirmwareUpdateStage.Downloading,
                BytesWritten = bytesWritten,
                TotalBytes = totalBytes,
            });

        public override void FirmwareDownloadComplete()
        {
            appLog.Log(
                "Firmware file received by printer - it is now flashing and rebooting. Keep it powered and on WiFi until this finishes.",
                LogLevel.Warning);
            progress.Report(new FirmwareUpdateProgress { Stage = FirmwareUpdateStage.AwaitingReboot });
        }

        public override void PrinterOnline(ZebraPrinterLinkOs printer, string firmwareVersion)
        {
            _reportedFirmwareVersion = firmwareVersion;
            var matches = string.Equals(firmwareVersion, expectedFirmwareVersion, StringComparison.OrdinalIgnoreCase);
            appLog.Log(
                matches
                    ? $"Printer back online with firmware version {firmwareVersion}."
                    : $"Printer reconnected reporting firmware version '{firmwareVersion}', not the expected '{expectedFirmwareVersion}' - the update did not take effect.",
                matches ? LogLevel.Success : LogLevel.Error);
            progress.Report(new FirmwareUpdateProgress { Stage = FirmwareUpdateStage.Complete });
        }

        // Called after UpdateFirmwareUnconditionally returns - throws (triggering a retry, or a
        // reported failure on the last attempt) if the printer never reported PrinterOnline, or came
        // back reporting a version other than the one this update was supposed to install.
        public void EnsureExpectedVersionInstalled()
        {
            if (!string.Equals(_reportedFirmwareVersion, expectedFirmwareVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Printer reconnected after the update reporting firmware version '{_reportedFirmwareVersion ?? "<none>"}', not the expected '{expectedFirmwareVersion}'.");
            }
        }
    }
}
