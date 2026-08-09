using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Installs and enables Zebra's "PDF Direct" virtual device (apl.enable "pdf"), as one more step in
/// PairAndConfigureWorkflow alongside applying WLAN settings - before the printer restarts, sharing
/// the same connection PairAndConfigureWorkflow opened for the whole pre-restart sequence rather than
/// reconnecting.
///
/// PDF Direct isn't built into the printer's base firmware; it's a separately-loaded "virtual
/// device" file (the bundled Virtual-Dev-PDF-v215.NRD) that must be downloaded to the printer once
/// before apl.enable "pdf" has any effect. Checked first via a plain apl.enable getvar (returns
/// "pdf" if already loaded and enabled, empty otherwise, per Zebra's own documented behavior) so a
/// printer that already has it doesn't re-transfer the file on every configuration run.
///
/// If not already enabled, a plain apl.enable "pdf" setvar is tried before pushing the file at
/// all - apl.enable can end up reset (e.g. by a factory reset or firmware update) while the
/// virtual device file itself is still resident on the printer from an earlier run, in which case
/// enabling alone succeeds and the (multi-second) file transfer is skipped entirely. The file is
/// only pushed if that attempt doesn't actually take effect.
///
/// The asset is extracted to a local file before this runs, not inside the session's RunAsync
/// delegate - that delegate is synchronous (run inside its own Task.Run), so the async file copy
/// can't happen partway through it.
/// </summary>
public sealed class LinkOsPdfDirectService(IAppLog appLog) : IPdfDirectService
{
    private const string PdfDirectAssetLogicalPath = "PDFDirect/Virtual-Dev-PDF-v215.NRD";
    private const string EnabledValue = "pdf";

    public async Task EnsureEnabledAsync(PrinterDevice device, IPrinterConnectionSession session, CancellationToken cancellationToken = default)
    {
        var localFilePath = await FirmwareAssetProvider.GetLocalFilePathAsync(PdfDirectAssetLogicalPath, cancellationToken);

        // Cast is safe - PrinterConnectionSessionFactory is the only production implementation of
        // IPrinterConnectionSession; see PrinterConnectionSession's doc comment for why the public
        // Core interface itself can't expose RunAsync (Core can't reference Zebra.Sdk.Comm.Connection).
        await ((PrinterConnectionSession)session).RunAsync(connection =>
        {
            var current = SGD.GET("apl.enable", connection)?.Trim();
            if (string.Equals(current, EnabledValue, StringComparison.OrdinalIgnoreCase))
            {
                appLog.Log("PDF Direct is already enabled.", LogLevel.Success);
                return;
            }

            appLog.Log("PDF Direct is not enabled - checking whether it can be enabled without reloading the virtual device file...");
            SGD.SET("apl.enable", EnabledValue, connection);
            var afterEnableAttempt = SGD.GET("apl.enable", connection)?.Trim();
            if (string.Equals(afterEnableAttempt, EnabledValue, StringComparison.OrdinalIgnoreCase))
            {
                appLog.Log("PDF Direct enabled - the virtual device file was already installed.", LogLevel.Success);
                return;
            }

            appLog.Log("PDF Direct virtual device file is not installed - loading it...");
            ZebraPrinterFactory.GetInstance(connection).SendFileContents(localFilePath);

            appLog.Log("Enabling PDF Direct...");
            SGD.SET("apl.enable", EnabledValue, connection);

            var actual = SGD.GET("apl.enable", connection)?.Trim();
            if (!string.Equals(actual, EnabledValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"PDF Direct did not enable - printer reports apl.enable = '{actual}'.");
            }

            appLog.Log("PDF Direct enabled.", LogLevel.Success);
        }, cancellationToken);
    }
}
