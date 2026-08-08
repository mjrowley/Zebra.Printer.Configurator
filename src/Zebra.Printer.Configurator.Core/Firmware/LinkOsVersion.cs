using System.Text.RegularExpressions;

namespace Zebra.Printer.Configurator.Core.Firmware;

/// <summary>
/// A Link-OS version (e.g. "7.6.2"), always directly comparable - unlike firmware versions, Link-OS
/// versions don't carry a per-model "branch" component, so ordering never needs a same-branch check.
/// </summary>
public readonly partial record struct LinkOsVersion(int Major, int Minor, int Micro) : IComparable<LinkOsVersion>
{
    public static bool TryParse(string? value, out LinkOsVersion version)
    {
        version = default;
        if (value is null)
        {
            return false;
        }

        var match = FormatRegex().Match(value);
        if (!match.Success)
        {
            return false;
        }

        version = new LinkOsVersion(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["micro"].Value));
        return true;
    }

    public int CompareTo(LinkOsVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Micro.CompareTo(other.Micro);
    }

    public override string ToString() => $"{Major}.{Minor}.{Micro}";

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<micro>\d+)$")]
    private static partial Regex FormatRegex();
}
