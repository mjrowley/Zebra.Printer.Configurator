namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// One SGD key/value pair read back from a printer for on-demand display in the UI (the "Check
/// Configuration" action). Value is already display-safe - sensitive keys (e.g. wlan.wpa.psk) are
/// redacted by the reader before this record is created, not by whatever renders it.
/// </summary>
public sealed record PrinterConfigurationValue(string Key, string Value);
