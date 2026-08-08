namespace Zebra.Printer.Configurator.Core.Firmware;

/// <summary>
/// The expected Link-OS/firmware baseline bundled with the app for one printer model, plus where to
/// find the actual firmware file (a MAUI raw asset under Resources/Raw) if an update is needed.
/// </summary>
public sealed record FirmwareBundle
{
    public required string ModelName { get; init; }

    public required LinkOsVersion ExpectedLinkOsVersion { get; init; }

    public required string ExpectedFirmwareVersion { get; init; }

    /// <summary>
    /// Matches the MauiAsset LogicalName ("%(RecursiveDir)%(Filename)%(Extension)", which strips the
    /// "Resources\Raw" prefix) for the bundled firmware file - e.g. "ZD421_Firmware/V93.21.49Z.zpl".
    /// </summary>
    public required string FirmwareAssetLogicalPath { get; init; }
}
