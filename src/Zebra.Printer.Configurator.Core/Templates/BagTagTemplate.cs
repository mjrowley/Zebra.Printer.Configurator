namespace Zebra.Printer.Configurator.Core.Templates;

/// <summary>
/// One bundled bag-tag ZPL format template - pairs the MauiAsset logical path (App project's
/// Resources/Raw/BagTagTemplates) with the exact filename the template stores itself as on the
/// printer's E: drive, declared inside the file itself via a "^DFE:&lt;PrinterFileName&gt;^FS" command.
/// </summary>
public sealed record BagTagTemplate(string LogicalAssetPath, string PrinterFileName);
