using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UI.Components;

/// <summary>
/// Drives CheckConfigurationResults (the potentially-long output table, kept in the page's
/// scrollable content area) - the host page (Pairing.razor/Result.razor) owns an instance directly
/// and calls SetLoading/SetResults/SetError itself from its own merged-status-read methods, rather
/// than routing through a dedicated trigger component; the "Recheck Configuration" trigger now lives
/// in PrinterActionsMenu's overflow menu instead.
/// </summary>
public sealed class CheckConfigurationState
{
    public bool Loading { get; private set; }

    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<PrinterConfigurationValue>? Values { get; private set; }

    public event Action? Changed;

    public void SetLoading()
    {
        Loading = true;
        ErrorMessage = null;
        Changed?.Invoke();
    }

    public void SetResults(IReadOnlyList<PrinterConfigurationValue> values)
    {
        Loading = false;
        Values = values;
        Changed?.Invoke();
    }

    public void SetError(string message)
    {
        Loading = false;
        ErrorMessage = message;
        Values = null;
        Changed?.Invoke();
    }
}
