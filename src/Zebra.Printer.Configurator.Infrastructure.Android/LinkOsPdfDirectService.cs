using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Installs and enables Zebra's "PDF Direct" virtual device (apl.enable "pdf") over Bluetooth, as
/// one more step in PairAndConfigureWorkflow alongside applying WLAN settings - before the printer
/// restarts, while Bluetooth is still the active connection.
///
/// PDF Direct isn't built into the printer's base firmware; it's a separately-loaded "virtual
/// device" file (the bundled Virtual-Dev-PDF-v215.NRD) that must be downloaded to the printer once
/// before apl.enable "pdf" has any effect. Checked first via a plain apl.enable getvar (returns
/// "pdf" if already loaded and enabled, empty otherwise, per Zebra's own documented behavior) so a
/// printer that already has it doesn't re-transfer the file on every configuration run.
///
/// The asset is extracted to a local file before the connection is opened, not inside the
/// PrinterConnectionRunner delegate - that delegate is synchronous (run inside its own Task.Run), so
/// the async file copy can't happen partway through it. Keeping extraction outside also means the
/// check-then-conditionally-install-then-enable-then-verify sequence below only needs a single
/// Bluetooth connect/disconnect cycle, not two.
/// </summary>
public sealed class LinkOsPdfDirectService(IPrinterConnectionModeProvider connectionModeProvider, IAppLog appLog, PrinterOperationCancellation cancellation) : IPdfDirectService
{
    private const string PdfDirectAssetLogicalPath = "PDFDirect/Virtual-Dev-PDF-v215.NRD";
    private const string EnabledValue = "pdf";

    public async Task EnsureEnabledAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        var localFilePath = await FirmwareAssetProvider.GetLocalFilePathAsync(PdfDirectAssetLogicalPath, cancellationToken);

        appLog.Log($"Connecting to printer over {connectionModeProvider.Mode} to check PDF Direct status...");
        await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            var current = SGD.GET("apl.enable", connection)?.Trim();
            if (string.Equals(current, EnabledValue, StringComparison.OrdinalIgnoreCase))
            {
                appLog.Log("PDF Direct is already enabled.", LogLevel.Success);
                return;
            }

            appLog.Log("PDF Direct is not enabled - loading virtual device file...");
            ZebraPrinterFactory.GetInstance(connection).SendFileContents(localFilePath);

            appLog.Log("Enabling PDF Direct...");
            SGD.SET("apl.enable", EnabledValue, connection);

            var actual = SGD.GET("apl.enable", connection)?.Trim();
            if (!string.Equals(actual, EnabledValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"PDF Direct did not enable - printer reports apl.enable = '{actual}'.");
            }

            appLog.Log("PDF Direct enabled.", LogLevel.Success);
        }, appLog, cancellation, cancellationToken);
    }
}
