using System.Security.Cryptography;
using System.Text;

namespace Zebra.Printer.Configurator.Core.Configuration;

/// <summary>
/// Derives the 64-hex-digit WPA/WPA2 pre-shared key that the printer's wlan.wpa.psk SGD command
/// actually expects. Zebra's own SGD Wireless Commands reference documents wlan.wpa.psk as taking
/// "64 hexadecimal digits" - not the raw ASCII passphrase - which is why setting it directly to the
/// user-entered password read back as confirmed (SGD.SET/GET round-tripped the value under that
/// key name) while never producing a key the radio could actually authenticate with.
///
/// The 64-hex-digit value is the standard WPA PSK: PBKDF2-HMAC-SHA1 of the ASCII passphrase, salted
/// with the SSID, 4096 iterations, 256-bit output - the same derivation used by wpa_passphrase and
/// every WPA-capable AP/client (IEEE 802.11i Annex H.6).
/// </summary>
public static class WpaPskDeriver
{
    private const int Iterations = 4096;
    private const int KeyLengthBytes = 32;

    public static string DeriveHexPsk(string ssid, string passphrase)
    {
        var salt = Encoding.UTF8.GetBytes(ssid);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA1, KeyLengthBytes);
        return Convert.ToHexString(key).ToLowerInvariant();
    }
}
