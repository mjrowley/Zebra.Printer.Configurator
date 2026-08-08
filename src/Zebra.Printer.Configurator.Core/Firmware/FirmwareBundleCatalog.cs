namespace Zebra.Printer.Configurator.Core.Firmware;

/// <summary>
/// The known set of bundled firmware baselines, one per supported model - currently just ZD421.
/// Adding the next model's bundled firmware is one more entry here, not a rewrite.
/// </summary>
public static class FirmwareBundleCatalog
{
    public static readonly IReadOnlyList<FirmwareBundle> All =
    [
        new FirmwareBundle
        {
            ModelName = "ZD421",
            ExpectedLinkOsVersion = new LinkOsVersion(7, 6, 2),
            ExpectedFirmwareVersion = "V93.21.49Z",
            FirmwareAssetLogicalPath = "ZD421_Firmware/V93.21.49Z.zpl",
        },
    ];

    /// <summary>
    /// Matches by substring (not exact equality) since the printer's reported device.product_name
    /// may carry extra branding/suffix text around the model name itself.
    /// </summary>
    public static FirmwareBundle? FindByProductName(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return null;
        }

        return All.FirstOrDefault(bundle => productName.Contains(bundle.ModelName, StringComparison.OrdinalIgnoreCase));
    }
}
