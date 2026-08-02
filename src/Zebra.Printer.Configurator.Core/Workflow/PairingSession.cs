using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Workflow;

/// <summary>
/// Carries state between the Pairing -> Configure -> Progress -> Result pages within a single
/// pairing attempt. Registered as a singleton - this is a single-window MAUI Blazor Hybrid app, so
/// there's exactly one attempt in flight at a time.
/// </summary>
public sealed class PairingSession
{
    public PrinterDevice? Device { get; set; }

    public WlanConfiguration? Configuration { get; set; }

    public void Reset()
    {
        Device = null;
        Configuration = null;
    }
}
