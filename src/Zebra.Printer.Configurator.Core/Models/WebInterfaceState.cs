namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>Whether the printer's web interface (ip.https.enable / ip.http.enable) is currently on.</summary>
public sealed record WebInterfaceState
{
    public required bool HttpsEnabled { get; init; }

    public required bool HttpEnabled { get; init; }

    public bool BothEnabled => HttpsEnabled && HttpEnabled;
}
