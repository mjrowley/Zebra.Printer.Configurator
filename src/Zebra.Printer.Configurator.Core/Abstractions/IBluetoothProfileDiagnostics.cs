namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Temporary diagnostic, not part of the pairing flow itself - logs Android's own Bluetooth profile
/// (A2DP/HFP) connection-state changes and raw ACL link events for a target device, to confirm or
/// rule out a theory about the OS's own "Can't connect" system dialog seen shortly after pairing.
/// Remove once that investigation is concluded - see the Infrastructure.Android implementation's own
/// doc comment for the full theory.
/// </summary>
public interface IBluetoothProfileDiagnostics
{
    /// <summary>Starts (or retargets, if already running) watching profile/ACL events for the given device address.</summary>
    void Start(string targetAddress);

    /// <summary>Stops watching. Safe to call even if not currently started.</summary>
    void Stop();
}
