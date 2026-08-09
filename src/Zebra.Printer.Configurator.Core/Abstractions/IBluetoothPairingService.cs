namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Establishes an OS-level Bluetooth bond with the printer, matching Zebra's "Tap &amp; Pair" numeric
/// SSP flow, but accepted automatically rather than asking the user to visually compare codes - see
/// the Infrastructure.Android implementation's own doc comment for why.
/// </summary>
public interface IBluetoothPairingService
{
    /// <summary>Returns true once bonded (immediately, if already bonded), or false if pairing failed or timed out.</summary>
    Task<bool> EnsurePairedAsync(string macAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the OS-level Bluetooth bond with the device, if one exists. Intended for use after a
    /// factory reset: the printer's own pairing key is reset along with everything else, so the
    /// phone's stored bond becomes stale and would otherwise make the next pairing attempt fail.
    /// Best-effort - failures are logged rather than thrown, since this is cleanup after an action
    /// that already succeeded.
    /// </summary>
    Task RemoveBondAsync(string macAddress, CancellationToken cancellationToken = default);
}
