using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Firmware;

public enum FirmwareUpdateRunState
{
    Idle,
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// Shared, app-wide record of the currently-running (or last) firmware update, updated by
/// FirmwareUpdateForegroundService (Android) and observed by PrinterVersionAlert.razor - the
/// notification and the in-app UI are two views onto this same state, not two disconnected
/// mechanisms. Registered as a singleton - same reasoning as PrinterConnectivityMonitor: one printer,
/// one update in flight at a time.
///
/// Also lets PrinterVersionAlert recognize on mount that an update is already running (e.g. the user
/// reopened the app mid-transfer) so it doesn't kick off a fresh Bluetooth version check that would
/// race the service's own connection to the same printer.
/// </summary>
public sealed class FirmwareUpdateStatusMonitor
{
    public FirmwareUpdateRunState State { get; private set; } = FirmwareUpdateRunState.Idle;

    public FirmwareUpdateProgress? Progress { get; private set; }

    public string? ErrorMessage { get; private set; }

    public event EventHandler? Changed;

    public void SetRunning()
    {
        State = FirmwareUpdateRunState.Running;
        Progress = null;
        ErrorMessage = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetProgress(FirmwareUpdateProgress progress)
    {
        Progress = progress;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSucceeded()
    {
        State = FirmwareUpdateRunState.Succeeded;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFailed(string errorMessage)
    {
        State = FirmwareUpdateRunState.Failed;
        ErrorMessage = errorMessage;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to Idle once the outcome has been consumed (e.g. PrinterVersionAlert has shown it).</summary>
    public void Reset()
    {
        State = FirmwareUpdateRunState.Idle;
        Progress = null;
        ErrorMessage = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
