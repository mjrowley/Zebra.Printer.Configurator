using Microsoft.Maui.ApplicationModel;
using Zebra.Printer.Configurator.Core.Abstractions;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Reads the version/build the App project's csproj declares (ApplicationDisplayVersion/
/// ApplicationVersion) via MAUI's AppInfo, rather than duplicating those values by hand anywhere.
/// </summary>
public sealed class AppVersionProvider : IAppVersionProvider
{
    public string VersionLabel => $"v{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
}
