using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using Java.Net;
using Microsoft.Maui.ApplicationModel;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Networking;
using Application = Android.App.Application;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Reads the host device's active WiFi connection via ConnectivityManager/LinkProperties, so its
/// netmask/gateway can be inherited into the printer's static IP configuration and its own IP
/// address can pre-fill the printer's static IP form field (the phone running this app is assumed to
/// already be on the target WiFi network, and the printer will join the same one). Uses the
/// application Context rather than the current Activity, so it has no Activity-lifecycle dependency.
/// </summary>
public sealed class HostNetworkInfoService : IHostNetworkInfoService
{
    public async Task<HostNetworkInfo?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var connectivityManager = (ConnectivityManager?)Application.Context.GetSystemService(Context.ConnectivityService);
        var activeNetwork = connectivityManager?.ActiveNetwork;
        if (connectivityManager is null || activeNetwork is null)
        {
            return null;
        }

        var capabilities = connectivityManager.GetNetworkCapabilities(activeNetwork);
        if (capabilities is null || !capabilities.HasTransport(TransportType.Wifi))
        {
            return null;
        }

        var linkProperties = connectivityManager.GetLinkProperties(activeNetwork);
        if (linkProperties is null)
        {
            return null;
        }

        var ipv4Address = linkProperties.LinkAddresses?
            .FirstOrDefault(a => a.Address is Inet4Address);
        if (ipv4Address is null)
        {
            return null;
        }

        var ipv4GatewayRoute = linkProperties.Routes?
            .FirstOrDefault(r => r.IsDefaultRoute && r.Gateway is Inet4Address);
        var gateway = ipv4GatewayRoute?.Gateway?.HostAddress;
        if (gateway is null)
        {
            return null;
        }

        return new HostNetworkInfo
        {
            HostIpAddress = ipv4Address.Address!.HostAddress ?? string.Empty,
            Netmask = Ipv4NetmaskConverter.FromPrefixLength(ipv4Address.PrefixLength),
            Gateway = gateway,
            Ssid = await TryGetCurrentSsidAsync(),
        };
    }

    // Android only returns the real SSID (rather than the literal placeholder "<unknown ssid>")
    // when location permission is granted - a platform privacy restriction, not a bug. This is only
    // ever a form default the user can freely overwrite, so a denied/unavailable permission should
    // leave it blank rather than block WiFi configuration entirely - hence the broad catch.
    private static async Task<string?> TryGetCurrentSsidAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (status != PermissionStatus.Granted)
            {
                return null;
            }

            var wifiManager = (WifiManager?)Application.Context.GetSystemService(Context.WifiService);

            // WifiManager.ConnectionInfo is deprecated (API 31+) in favor of registering a
            // NetworkCallback and reading WifiInfo off its NetworkCapabilities - real plumbing for a
            // feature that's only ever a convenience form default here. It's deprecated, not
            // removed, and still returns real data on every OS version this app targets (33-36).
#pragma warning disable CA1422
            var ssid = wifiManager?.ConnectionInfo?.SSID;
#pragma warning restore CA1422
            if (string.IsNullOrEmpty(ssid) || ssid == "<unknown ssid>")
            {
                return null;
            }

            return ssid.Trim('"');
        }
        catch
        {
            return null;
        }
    }
}
