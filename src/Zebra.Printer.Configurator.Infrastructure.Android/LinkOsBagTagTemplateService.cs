using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Templates;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Checks for and pushes the bundled bag-tag ZPL format templates (BagTagTemplateCatalog) to the
/// printer, over whichever transport is currently active (Bluetooth or WiFi) - these are small text
/// files (under 2KB each), unlike the firmware update's 41MB transfer that specifically needs WiFi.
///
/// Existence is checked via ZebraPrinter.RetrieveFileNames (the Zebra.Sdk.Device.FileUtil interface,
/// implemented by the same ZebraPrinter instance SendFileContents is on - both work against a
/// Connection this app already has open) rather than Zebra.Sdk.Printer.PrinterUtil's static
/// file-listing helpers, which take a raw connection string and open their own separate connection
/// internally - a second concurrent Bluetooth connection is exactly the class of bug this app spent a
/// long investigation chasing elsewhere (see BluetoothPairingService's own doc comment).
///
/// Each template is pushed as-is via SendFileContents - it declares its own destination filename on
/// the printer's E: drive through an embedded ^DFE:...^FS command, so the printer's own ZPL
/// interpreter handles storing (and overwriting) it under that name; no separate "store as" call is
/// needed, mirroring LinkOsPdfDirectService's use of the same method for its virtual device file.
/// </summary>
public sealed class LinkOsBagTagTemplateService(
    IBluetoothPermissionService bluetoothPermissionService,
    IPrinterConnectionModeProvider connectionModeProvider,
    IAppLog appLog) : IBagTagTemplateService
{
    public async Task<IReadOnlyList<string>> GetExistingTemplateFileNamesAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        appLog.Log("Checking printer for existing bag tag templates...");
        var existingFileNames = await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            var reportedNames = ZebraPrinterFactory.GetInstance(connection).RetrieveFileNames(["ZPL"]) ?? [];
            return BagTagTemplateCatalog.All
                .Select(template => template.PrinterFileName)
                .Where(fileName => reportedNames.Any(reported => MatchesPrinterFileName(reported, fileName)))
                .ToList();
        }, appLog, cancellation: null, cancellationToken);

        appLog.Log(
            existingFileNames.Count == 0
                ? "No existing bag tag templates found on the printer."
                : $"{existingFileNames.Count} bag tag template(s) already exist on the printer: {string.Join(", ", existingFileNames)}",
            existingFileNames.Count == 0 ? LogLevel.Info : LogLevel.Warning);

        return existingFileNames;
    }

    public async Task DeployTemplatesAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        await BluetoothPermissionGuard.EnsureGrantedAsync(bluetoothPermissionService, connectionModeProvider, appLog, cancellationToken);

        // Extracted before opening the connection - the connection's own delegate below is
        // synchronous (runs inside its own Task.Run), so async asset extraction can't happen partway
        // through it, matching LinkOsPdfDirectService's own doc comment on the same constraint.
        var localFilePaths = new List<(string LocalPath, string PrinterFileName)>();
        foreach (var template in BagTagTemplateCatalog.All)
        {
            var localPath = await BundledAssetProvider.GetLocalFilePathAsync(template.LogicalAssetPath, cancellationToken);
            localFilePaths.Add((localPath, template.PrinterFileName));
        }

        appLog.Log($"Sending {localFilePaths.Count} bag tag template(s) to the printer...");
        await PrinterConnectionRunner.RunAsync(device, connectionModeProvider, connection =>
        {
            var printer = ZebraPrinterFactory.GetInstance(connection);
            foreach (var (localPath, printerFileName) in localFilePaths)
            {
                appLog.Log($"Sending {printerFileName}...");
                printer.SendFileContents(localPath);
            }
        }, appLog, cancellation: null, cancellationToken);

        appLog.Log("Bag tag templates sent to the printer.", LogLevel.Success);
    }

    // The exact format ZebraPrinter.RetrieveFileNames reports filenames in (e.g. with or without a
    // drive prefix like "E:") isn't confirmed from documentation alone - matched defensively by
    // filename only, ignoring any drive prefix, so this doesn't depend on getting that format exactly
    // right on the first on-device test.
    private static bool MatchesPrinterFileName(string reportedFileName, string expectedFileName)
    {
        var separatorIndex = reportedFileName.IndexOf(':');
        var normalized = separatorIndex >= 0 ? reportedFileName[(separatorIndex + 1)..] : reportedFileName;
        return string.Equals(normalized, expectedFileName, StringComparison.OrdinalIgnoreCase);
    }
}
