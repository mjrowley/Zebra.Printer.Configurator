namespace Zebra.Printer.Configurator.Core.Templates;

/// <summary>
/// The bundled bag-tag ZPL format templates (App project's Resources/Raw/BagTagTemplates). Each
/// template's PrinterFileName is exactly what its own embedded ^DFE:...^FS command names, confirmed
/// by inspecting all three bundled files - kept here rather than derived at runtime since it's a
/// small, fixed set that doesn't change without a code change (adding a new template) anyway.
/// </summary>
public static class BagTagTemplateCatalog
{
    public static readonly IReadOnlyList<BagTagTemplate> All =
    [
        new BagTagTemplate("BagTagTemplates/FetchCCT.zpl", "FetchCCT.ZPL"),
        new BagTagTemplate("BagTagTemplates/FetchFDT.zpl", "FetchFDT.ZPL"),
        new BagTagTemplate("BagTagTemplates/FetchSDT.zpl", "FetchSDT.ZPL"),
    ];
}
