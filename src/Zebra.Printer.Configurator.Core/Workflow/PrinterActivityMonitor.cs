namespace Zebra.Printer.Configurator.Core.Workflow;

/// <summary>
/// Cross-page "is the app currently doing something with the printer" signal, for controls that
/// need to know this regardless of which page is showing - e.g. BackToPairingButton, rendered in
/// the header and visible on every page. Complements (doesn't replace) the page-local
/// IsActiveChanged/BlockingChanged callbacks that FactoryResetPanel/BagTagTemplatesPanel/
/// CheckConfigurationButton/PrinterVersionAlert already report to their hosting page - those still
/// drive each page's own sibling-button disabling, but were never visible outside that page.
///
/// PairAndConfigureWorkflow.State and FirmwareUpdateStatusMonitor.State are already app-wide
/// singletons and are NOT routed through this - only the four sources above, which previously had
/// no cross-page visibility at all.
///
/// Token-keyed (not a plain counter) so more than one source can be active without one's Dispose()
/// incorrectly clearing another's, and so ActiveSources is inspectable if a caller is ever
/// mysteriously stuck disabled.
/// </summary>
public sealed class PrinterActivityMonitor
{
    private readonly Dictionary<object, string> _active = [];

    public bool IsBusy => _active.Count > 0;

    public IReadOnlyCollection<string> ActiveSources => _active.Values;

    public event EventHandler? Changed;

    public IDisposable Begin(string source)
    {
        var token = new object();
        _active[token] = source;
        Changed?.Invoke(this, EventArgs.Empty);
        return new Registration(this, token);
    }

    private void End(object token)
    {
        if (_active.Remove(token))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Registration(PrinterActivityMonitor owner, object token) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.End(token);
        }
    }
}
