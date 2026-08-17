using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Pages;

namespace Zebra.Printer.Configurator.ComponentTests.Pages;

public class ConfigureTests : BunitContext
{
    private readonly IHostNetworkInfoService _hostNetworkInfoService = Substitute.For<IHostNetworkInfoService>();
    private readonly IPrinterModelReader _modelReader = Substitute.For<IPrinterModelReader>();
    private readonly PairingSession _session = new() { Device = new PrinterDevice { BluetoothMacAddress = "AABBCCDDEEFF" } };

    public ConfigureTests()
    {
        Services.AddSingleton(_hostNetworkInfoService);
        Services.AddSingleton(_modelReader);
        Services.AddSingleton(_session);

        // Host network detection now happens on page load rather than at submit time, so every test
        // needs a default happy-path stub unless it's specifically exercising the error/prefill case.
        _hostNetworkInfoService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new HostNetworkInfo { HostIpAddress = "192.168.1.42", Netmask = "255.255.255.0", Gateway = "192.168.1.1" });

        // Same reasoning - the live printer-model read also happens on page load. "ZD421c" (not
        // the "ZD421" fallback default) so tests asserting the field's value can tell whether it
        // came from a genuine live read or the hardcoded fallback.
        _modelReader.ReadModelNameAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns("ZD421c");
    }

    private static void FillIp(IRenderedComponent<Configure> cut, string ip)
    {
        var octets = ip.Split('.');
        for (var i = 0; i < 4; i++)
        {
            cut.Find($"#ip-octet-{i + 1}").Input(i < octets.Length ? octets[i] : string.Empty);
        }
    }

    [Fact]
    public async Task Submit_WithInvalidSsid_ShowsValidationErrorAndDoesNotNavigate()
    {
        var cut = Render<Configure>();

        cut.Find("#ssid").Change("");
        cut.Find("#password").Change("correcthorsebatterystaple");
        FillIp(cut, "192.168.1.50");
        await cut.Find("form").SubmitAsync();

        Assert.Contains("SSID is required", cut.Markup);
        Assert.Null(_session.Configuration);
    }

    [Fact]
    public async Task Submit_WithInvalidPassword_ShowsValidationErrorAndDoesNotNavigate()
    {
        var cut = Render<Configure>();

        cut.Find("#ssid").Change("Warehouse-WiFi");
        cut.Find("#password").Change("short");
        FillIp(cut, "192.168.1.50");
        await cut.Find("form").SubmitAsync();

        Assert.Contains("between 8 and 63 characters", cut.Markup);
        Assert.Null(_session.Configuration);
    }

    [Fact]
    public async Task Submit_WithIncompleteIpAddress_ShowsValidationErrorAndDoesNotNavigate()
    {
        // The segmented input can't produce free-text garbage like "not-an-ip" anymore (each box
        // only accepts digits), so the reachable invalid case is leaving octets blank.
        var cut = Render<Configure>();

        cut.Find("#ssid").Change("Warehouse-WiFi");
        cut.Find("#password").Change("correcthorsebatterystaple");
        cut.Find("#ip-octet-1").Input("192");
        cut.Find("#ip-octet-2").Input("168");
        await cut.Find("form").SubmitAsync();

        Assert.Contains("dotted-quad IPv4 format", cut.Markup);
        Assert.Null(_session.Configuration);
    }

    [Fact]
    public void WhenHostNotOnWifi_ShowsBlockingErrorAndDoesNotRenderForm()
    {
        _hostNetworkInfoService.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns((HostNetworkInfo?)null);

        var cut = Render<Configure>();

        Assert.NotNull(cut.Find("[data-testid='host-network-error']"));
        Assert.Empty(cut.FindAll("#ssid"));
        Assert.Null(_session.Configuration);
    }

    [Fact]
    public void WhenHostNetworkHasSsid_PrefillsSsidField()
    {
        _hostNetworkInfoService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new HostNetworkInfo { HostIpAddress = "192.168.1.42", Netmask = "255.255.255.0", Gateway = "192.168.1.1", Ssid = "Warehouse-WiFi" });

        var cut = Render<Configure>();

        Assert.Equal("Warehouse-WiFi", cut.Find("#ssid").GetAttribute("value"));
    }

    [Fact]
    public void WhenHostNetworkHasNoSsid_LeavesSsidFieldEmpty()
    {
        // HostNetworkInfo.Ssid is null whenever location permission wasn't granted - the SSID is
        // only ever a convenience default, so this must not block or pre-fill anything odd.
        var cut = Render<Configure>();

        Assert.Equal(string.Empty, cut.Find("#ssid").GetAttribute("value"));
    }

    [Fact]
    public void WhenHostNetworkHasIpAddress_PrefillsFirstThreeOctetsOfStaticIp()
    {
        // The default ctor stub already returns HostIpAddress "192.168.1.42" - the printer is
        // assumed to join the same subnet, so only the first 3 octets should be pre-filled,
        // leaving the last one for the user to enter.
        var cut = Render<Configure>();

        Assert.Equal("192", cut.Find("#ip-octet-1").GetAttribute("value"));
        Assert.Equal("168", cut.Find("#ip-octet-2").GetAttribute("value"));
        Assert.Equal("1", cut.Find("#ip-octet-3").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("#ip-octet-4").GetAttribute("value"));
    }

    [Fact]
    public void WhenHostIpAddressIsUnavailable_LeavesStaticIpFieldEmpty()
    {
        _hostNetworkInfoService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new HostNetworkInfo { HostIpAddress = string.Empty, Netmask = "255.255.255.0", Gateway = "192.168.1.1" });

        var cut = Render<Configure>();

        Assert.Equal(string.Empty, cut.Find("#ip-octet-1").GetAttribute("value"));
    }

    [Fact]
    public async Task Submit_WithValidFormAndHostOnWifi_PopulatesSessionAndNavigatesToProgress()
    {
        _hostNetworkInfoService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new HostNetworkInfo { HostIpAddress = "192.168.1.42", Netmask = "255.255.255.0", Gateway = "192.168.1.1" });
        var cut = Render<Configure>();

        cut.Find("#ssid").Change("Warehouse-WiFi");
        cut.Find("#password").Change("correcthorsebatterystaple");
        FillIp(cut, "192.168.1.50");
        await cut.Find("form").SubmitAsync();

        Assert.NotNull(_session.Configuration);
        Assert.Equal("ZD421c", _session.Configuration!.PrinterName);
        Assert.Equal("Warehouse-WiFi", _session.Configuration.Ssid);
        Assert.Equal("correcthorsebatterystaple", _session.Configuration.Password);
        Assert.Equal(WlanIpAddressMode.Static, _session.Configuration.IpAddressMode);
        Assert.Equal("192.168.1.50", _session.Configuration.StaticIpAddress);
        Assert.Equal("255.255.255.0", _session.Configuration.Netmask);
        Assert.Equal("192.168.1.1", _session.Configuration.Gateway);

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/progress", navigation.Uri);
    }

    [Fact]
    public void InitialRender_DefaultsToStaticIpModeWithFieldVisible()
    {
        var cut = Render<Configure>();

        Assert.True(cut.Find("[data-testid='ip-mode-static']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='ip-mode-dhcp']").HasAttribute("checked"));
        Assert.NotNull(cut.Find("#ip-octet-1"));
    }

    [Fact]
    public void ClickingDhcpRadio_HidesStaticIpFieldAndChecksDhcp()
    {
        var cut = Render<Configure>();

        cut.Find("[data-testid='ip-mode-dhcp']").Click();

        Assert.True(cut.Find("[data-testid='ip-mode-dhcp']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='ip-mode-static']").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("#ip-octet-1"));
    }

    [Fact]
    public void ClickingDhcpThenStaticRadio_ShowsStaticIpFieldAgain()
    {
        var cut = Render<Configure>();
        cut.Find("[data-testid='ip-mode-dhcp']").Click();

        cut.Find("[data-testid='ip-mode-static']").Click();

        Assert.NotNull(cut.Find("#ip-octet-1"));
    }

    [Fact]
    public async Task Submit_WithDhcpMode_PopulatesSessionWithDhcpModeAndDoesNotRequireAValidStaticIp()
    {
        // The host-network prefill leaves StaticIpAddress as an incomplete "192.168.1" (first 3
        // octets only, see WhenHostNetworkHasIpAddress_PrefillsFirstThreeOctetsOfStaticIp) - an
        // invalid IPv4Validator.Validate result if it were ever checked, confirming DHCP mode really
        // does skip that validation rather than just happening to have a valid value lying around.
        var cut = Render<Configure>();
        cut.Find("[data-testid='ip-mode-dhcp']").Click();
        cut.Find("#ssid").Change("Warehouse-WiFi");
        cut.Find("#password").Change("correcthorsebatterystaple");

        await cut.Find("form").SubmitAsync();

        Assert.NotNull(_session.Configuration);
        Assert.Equal(WlanIpAddressMode.Dhcp, _session.Configuration!.IpAddressMode);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/progress", navigation.Uri);
    }

    [Fact]
    public async Task Submit_WithStaticModeSelected_StillRequiresAValidIp()
    {
        var cut = Render<Configure>();
        cut.Find("#ssid").Change("Warehouse-WiFi");
        cut.Find("#password").Change("correcthorsebatterystaple");
        cut.Find("[data-testid='ip-mode-dhcp']").Click();
        cut.Find("[data-testid='ip-mode-static']").Click();
        cut.Find("#ip-octet-1").Input("192");
        cut.Find("#ip-octet-2").Input("168");

        await cut.Find("form").SubmitAsync();

        Assert.Contains("dotted-quad IPv4 format", cut.Markup);
        Assert.Null(_session.Configuration);
    }

    [Fact]
    public void WhenModelReadSucceeds_PrefillsPrinterNameField()
    {
        _modelReader.ReadModelNameAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns("ZD621");

        var cut = Render<Configure>();

        Assert.Equal("ZD621", cut.Find("#printer-name").GetAttribute("value"));
    }

    [Fact]
    public void WhenModelReadFails_FallsBackToDefaultPrinterName()
    {
        _modelReader.ReadModelNameAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new InvalidOperationException("simulated read failure"));

        var cut = Render<Configure>();

        Assert.Equal("ZD421", cut.Find("#printer-name").GetAttribute("value"));
    }

    [Fact]
    public void WhenModelReadReturnsBlank_FallsBackToDefaultPrinterName()
    {
        _modelReader.ReadModelNameAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var cut = Render<Configure>();

        Assert.Equal("ZD421", cut.Find("#printer-name").GetAttribute("value"));
    }

    [Fact]
    public void ClickingBack_NavigatesToPairingWithoutClearingSession()
    {
        var device = _session.Device;
        var cut = Render<Configure>();

        cut.Find("[data-testid='configure-back-button']").Click();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
        Assert.Same(device, _session.Device);
    }

    [Fact]
    public void WhenNoDeviceInSession_RedirectsToPairing()
    {
        _session.Device = null;

        Render<Configure>();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
    }
}
