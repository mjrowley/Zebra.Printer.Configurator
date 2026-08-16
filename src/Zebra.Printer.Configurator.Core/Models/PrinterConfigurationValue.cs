using Zebra.Printer.Configurator.Core.Configuration;

namespace Zebra.Printer.Configurator.Core.Models;

/// <summary>
/// One SGD key/value pair read back from a printer for on-demand display in the UI (the "Check
/// Configuration" action). Value is already display-safe - sensitive keys (e.g. wlan.wpa.psk) are
/// redacted by the reader before this record is created, not by whatever renders it. Match defaults
/// to Informational - only LinkOsPrinterStatusReader's merged read (the one that actually feeds the
/// colour-coded Check Configuration screen) computes a real value; other constructors, including
/// test call sites, don't need to care.
/// </summary>
public sealed record PrinterConfigurationValue(string Key, string Value, ConfigurationValueMatch Match = ConfigurationValueMatch.Informational);
