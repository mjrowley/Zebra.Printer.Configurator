namespace Zebra.Printer.Configurator.Core.Networking;

/// <summary>
/// Converts a CIDR prefix length (as reported by Android's LinkProperties for the host device's
/// active WiFi connection) into a dotted-decimal IPv4 netmask, since that's the form the printer's
/// wlan.ip.netmask SGD command expects.
/// </summary>
public static class Ipv4NetmaskConverter
{
    public static string FromPrefixLength(int prefixLength)
    {
        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength), prefixLength, "IPv4 prefix length must be between 0 and 32.");
        }

        // A shift of 32 is undefined behavior for a 32-bit value in C#, so the all-zero mask
        // (prefix length 0) is handled explicitly instead of shifting by the full width.
        var mask = prefixLength == 0 ? 0u : 0xFFFFFFFFu << (32 - prefixLength);

        return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
    }
}
