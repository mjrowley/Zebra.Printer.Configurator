using Android.Content;
using Android.Net;
using Java.Net;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Networking;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Reads the host device's active WiFi connection via ConnectivityManager/LinkProperties, so its
/// netmask/gateway can be inherited into the printer's static IP configuration (the phone running
/// this app is assumed to already be on the target WiFi network). Uses the application Context
/// rather than the current Activity, so it has no Activity-lifecycle dependency.
/// </summary>
public sealed class HostNetworkInfoService : IHostNetworkInfoService
{
    public Task<HostNetworkInfo?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var connectivityManager = (ConnectivityManager?)Application.Context.GetSystemService(Context.ConnectivityService);
        var activeNetwork = connectivityManager?.ActiveNetwork;
        if (connectivityManager is null || activeNetwork is null)
        {
            return Task.FromResult<HostNetworkInfo?>(null);
        }

        var capabilities = connectivityManager.GetNetworkCapabilities(activeNetwork);
        if (capabilities is null || !capabilities.HasTransport(TransportType.Wifi))
        {
            return Task.FromResult<HostNetworkInfo?>(null);
        }

        var linkProperties = connectivityManager.GetLinkProperties(activeNetwork);
        if (linkProperties is null)
        {
            return Task.FromResult<HostNetworkInfo?>(null);
        }

        var ipv4Address = linkProperties.LinkAddresses?
            .FirstOrDefault(a => a.Address is Inet4Address);
        if (ipv4Address is null)
        {
            return Task.FromResult<HostNetworkInfo?>(null);
        }

        var ipv4GatewayRoute = linkProperties.Routes?
            .FirstOrDefault(r => r.IsDefaultRoute && r.Gateway is Inet4Address);
        var gateway = ipv4GatewayRoute?.Gateway?.HostAddress;
        if (gateway is null)
        {
            return Task.FromResult<HostNetworkInfo?>(null);
        }

        var hostNetworkInfo = new HostNetworkInfo
        {
            Netmask = Ipv4NetmaskConverter.FromPrefixLength(ipv4Address.PrefixLength),
            Gateway = gateway,
        };

        return Task.FromResult<HostNetworkInfo?>(hostNetworkInfo);
    }
}
