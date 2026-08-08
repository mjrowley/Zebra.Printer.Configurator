using System.Text.RegularExpressions;

namespace Zebra.Printer.Configurator.Core.Firmware;

/// <summary>
/// A Zebra printer OS firmware version (e.g. "V93.21.49Z") - Branch identifies the OS release
/// family/train (confirmed against the bundled Link-OS 7.6.2 release notes: "V93" is one specific
/// branch shared by ZD421/ZD621 direct-thermal/thermal-transfer variants, while other models use
/// entirely different, unrelated branches such as V100/V101). Two versions are only meaningfully
/// comparable within the same branch - the release notes confirm Major/Minor increase monotonically
/// within a branch (e.g. V93.21.06Z through V93.21.49Z, listed newest-first), but nothing ties one
/// branch's numbering to another's. Suffix (a single trailing letter in every example found, always
/// "Z") isn't used for ordering - there's no evidence it varies meaningfully.
/// </summary>
public readonly partial record struct FirmwareVersion(int Branch, int Major, int Minor, string Suffix)
{
    public static bool TryParse(string? value, out FirmwareVersion version)
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

        version = new FirmwareVersion(
            int.Parse(match.Groups["branch"].Value),
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            match.Groups["suffix"].Value);
        return true;
    }

    public override string ToString() => $"V{Branch}.{Major}.{Minor}{Suffix}";

    [GeneratedRegex(@"^V(?<branch>\d+)\.(?<major>\d+)\.(?<minor>\d+)(?<suffix>[A-Za-z]*)$")]
    private static partial Regex FormatRegex();
}
