namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Repeatedly re-probes a printer's WiFi reachability in the background, updating
/// PrinterConnectivityMonitor.Wifi on every attempt - kept behind an interface (rather than used
/// concretely) so UI component tests can substitute a no-op fake instead of running a real polling
/// loop against the network.
/// </summary>
public interface IWifiConnectivityMonitor
{
    /// <summary>Starts polling the given IP, replacing any previously started target.</summary>
    void Start(string ipAddress);

    /// <summary>Stops polling - no-op if not currently running.</summary>
    void Stop();
}
