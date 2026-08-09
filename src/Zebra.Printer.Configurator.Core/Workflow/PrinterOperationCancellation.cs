namespace Zebra.Printer.Configurator.Core.Workflow;

/// <summary>
/// Shared source of truth for cancelling an in-progress PairAndConfigureWorkflow run - the header's
/// Cancel button and Progress.razor reach this through DI rather than each other directly.
///
/// A CancellationToken alone only takes effect *between* awaited steps - every actual Bluetooth/WiFi
/// operation in this app is one blocking synchronous Zebra SDK call (SGD.SET, SendFileContents, ...)
/// running inside its own Task.Run, so Cancel() also force-closes whichever Connection is currently
/// open, via whatever close action the runner in progress registered - the same technique as closing
/// a socket from another thread to unblock a thread stuck in a blocking read/write on it.
/// </summary>
public sealed class PrinterOperationCancellation
{
    private CancellationTokenSource _cts = new();
    private Action? _closeActiveConnection;

    public CancellationToken Token => _cts.Token;

    /// <summary>Call once at the start of each new workflow run, for a fresh, uncancelled token.</summary>
    public void Begin() => _cts = new CancellationTokenSource();

    public void Cancel()
    {
        _cts.Cancel();
        _closeActiveConnection?.Invoke();
    }

    /// <summary>
    /// Registers the action that force-closes whatever Connection is open right now - call this
    /// immediately after Connection.Open() succeeds, and dispose the result (unregistering) in the
    /// same finally block that closes the connection normally.
    /// </summary>
    public IDisposable TrackActiveConnection(Action close)
    {
        _closeActiveConnection = close;
        return new Unregister(this);
    }

    private sealed class Unregister(PrinterOperationCancellation owner) : IDisposable
    {
        public void Dispose() => owner._closeActiveConnection = null;
    }
}
