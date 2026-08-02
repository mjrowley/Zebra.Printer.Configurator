namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Establishes an OS-level Bluetooth bond with the printer, matching Zebra's own "Tap &amp; Pair"
/// flow: the printer displays a numeric code, the phone must display the same code, and the user
/// confirms they match. <see cref="PairingCodeRequested"/> fires when that confirmation is needed;
/// the UI is expected to show the code and resolve <see cref="PairingCodeRequestedEventArgs.Response"/>
/// with the user's answer.
/// </summary>
public interface IBluetoothPairingService
{
    event EventHandler<PairingCodeRequestedEventArgs>? PairingCodeRequested;

    /// <summary>Returns true once bonded (immediately, if already bonded), or false if pairing failed, was rejected, or timed out.</summary>
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

public sealed class PairingCodeRequestedEventArgs(string pairingCode) : EventArgs
{
    public string PairingCode { get; } = pairingCode;

    // RunContinuationsAsynchronously: without it, TrySetResult can run the awaiter's continuation
    // synchronously on the calling thread, which for a UI event handler means code after
    // TrySetResult in that same handler could run interleaved with (and be overwritten by) the
    // continuation - a classic TaskCompletionSource reentrancy hazard.
    public TaskCompletionSource<bool> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
