using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Pushes the app's bundled bag-tag ZPL format templates (BagTagTemplateCatalog) to the printer's E:
/// drive. Each template declares its own destination filename via an embedded ZPL command, so
/// deploying is just sending the raw file contents - the printer's own ZPL interpreter handles
/// storing (and overwriting) it under that name.
/// </summary>
public interface IBagTagTemplateService
{
    /// <summary>
    /// Returns which of the bundled templates' PrinterFileNames already exist on the printer (empty
    /// if none do) - checked first so the caller can warn the user before DeployTemplatesAsync
    /// overwrites them.
    /// </summary>
    Task<IReadOnlyList<string>> GetExistingTemplateFileNamesAsync(PrinterDevice device, CancellationToken cancellationToken = default);

    /// <summary>Pushes every bundled template to the printer, overwriting any that already exist.</summary>
    Task DeployTemplatesAsync(PrinterDevice device, CancellationToken cancellationToken = default);
}
