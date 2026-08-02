namespace Zebra.Printer.Configurator.Core.Abstractions;

/// <summary>
/// Exposes the running app's own version, shown in the UI so an on-device bug report can be pinned
/// to an actual build rather than inferred from when the user last updated.
/// </summary>
public interface IAppVersionProvider
{
    string VersionLabel { get; }
}
