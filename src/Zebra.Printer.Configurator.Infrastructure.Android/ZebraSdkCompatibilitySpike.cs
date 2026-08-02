using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Phase 1 spike proving the Zebra.Printer.SDK NuGet package's real API surface
/// (BluetoothConnection, TcpConnection, SGD.GET/SET/DO) resolves and compiles against
/// net10.0-android. Superseded by the real service implementations in later phases.
/// </summary>
internal static class ZebraSdkCompatibilitySpike
{
    public static string ReadWlanState(string bluetoothMacAddress)
    {
        Connection connection = new BluetoothConnection(bluetoothMacAddress);
        connection.Open();
        try
        {
            return SGD.GET("wlan.state", connection);
        }
        finally
        {
            connection.Close();
        }
    }

    public static void ApplyStaticIpAndRestart(string ipAddress, int port, string ssid, string password, string netmask, string gateway)
    {
        Connection connection = new TcpConnection(ipAddress, port);
        connection.Open();
        try
        {
            SGD.SET("wlan.ip.default_addr_enable", "off", connection);
            SGD.SET("wlan.ssid", ssid, connection);
            SGD.SET("wlan.password", password, connection);
            SGD.SET("wlan.ip.addr", ipAddress, connection);
            SGD.SET("wlan.ip.netmask", netmask, connection);
            SGD.SET("wlan.ip.gateway", gateway, connection);
            SGD.DO("device.restart", string.Empty, connection);
        }
        finally
        {
            connection.Close();
        }
    }
}
