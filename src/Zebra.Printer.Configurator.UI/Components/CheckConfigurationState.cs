using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UI.Components;

/// <summary>
/// Shared state between CheckConfigurationButton (the trigger, kept in a page's sticky header
/// alongside its other primary buttons) and CheckConfigurationResults (the potentially-long output
/// table, kept in the page's scrollable content area) - split into two components so the button and
/// function name stay pinned at the top of the section while a long results table scrolls
/// independently, per the section-2 layout requirement.
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
