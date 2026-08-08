using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Zebra.Sdk.Printer.FirmwareUpdaterLinkOs.UpdateFirmware needs a real filesystem path, but the
/// bundled firmware file is a MAUI raw asset packaged inside the APK - this extracts it to the
/// app's cache directory once (a 41MB copy isn't something to repeat on every version check) using
/// Android's AssetManager directly (matching the MauiAsset LogicalName format,
/// "%(RecursiveDir)%(Filename)%(Extension)", already used elsewhere in the app) rather than adding a
/// Microsoft.Maui.Essentials package reference this project doesn't currently have.
/// </summary>
internal static class FirmwareAssetProvider
{
    public static async Task<string> GetLocalFilePathAsync(string logicalAssetPath, CancellationToken cancellationToken = default)
    {
        var cacheDir = Application.Context.CacheDir!.AbsolutePath;
        var fileName = Path.GetFileName(logicalAssetPath);
        var destinationPath = Path.Combine(cacheDir, fileName);

        if (File.Exists(destinationPath))
        {
            return destinationPath;
        }

        // Extracted to a temp file and moved into place only once the full copy succeeds - an
        // interrupted copy (app killed mid-extraction) then never leaves a partial file at
        // destinationPath for a later check to mistake as already-cached.
        var tempPath = destinationPath + ".tmp";
        using (var assetStream = Application.Context.Assets!.Open(logicalAssetPath))
        await using (var destinationStream = File.Create(tempPath))
        {
            await assetStream.CopyToAsync(destinationStream, cancellationToken);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        return destinationPath;
    }
}
