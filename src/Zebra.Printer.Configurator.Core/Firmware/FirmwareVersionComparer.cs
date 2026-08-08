namespace Zebra.Printer.Configurator.Core.Firmware;

public enum FirmwareVersionComparison
{
    Older,
    Equal,
    Newer,

    /// <summary>
    /// The two versions are on different OS branches (or one/both didn't parse) - "higher" or
    /// "lower" isn't meaningful across branches, so this is treated as its own outcome rather than
    /// guessed at.
    /// </summary>
    Incomparable,
}

public static class FirmwareVersionComparer
{
    public static FirmwareVersionComparison Compare(FirmwareVersion actual, FirmwareVersion expected)
    {
        if (actual.Branch != expected.Branch)
        {
            return FirmwareVersionComparison.Incomparable;
        }

        if (actual.Major != expected.Major)
        {
            return actual.Major > expected.Major ? FirmwareVersionComparison.Newer : FirmwareVersionComparison.Older;
        }

        if (actual.Minor != expected.Minor)
        {
            return actual.Minor > expected.Minor ? FirmwareVersionComparison.Newer : FirmwareVersionComparison.Older;
        }

        return FirmwareVersionComparison.Equal;
    }
}
