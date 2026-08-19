using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Pages;

namespace Zebra.Printer.Configurator.ComponentTests.Pages;

public class PrinterInfoTests : BunitContext
{
    private static readonly PrinterDevice Device = new()
    {
        BluetoothMacAddress = "AABBCCDDEEFF",
        SerialNumber = "12345",
        WifiMacAddress = "112233445566",
    };

    private readonly IPrinterStatusReader _statusReader = Substitute.For<IPrinterStatusReader>();
    private readonly PairingSession _session = new() { Device = Device };

    public PrinterInfoTests()
    {
        Services.AddSingleton(_statusReader);
        Services.AddSingleton(_session);
    }

    private static PrinterStatus StatusWith(params (string Key, string Value)[] configurationValues) => new()
    {
        VersionResult = new PrinterVersionCheckResult
        {
            Outcome = PrinterVersionOutcome.UpToDate,
            LinkOsVersionFound = "7.6.2",
            FirmwareVersionFound = "V93.21.49Z",
        },
        WebInterfaceState = new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true },
        ConfigurationValues = configurationValues.Select(v => new PrinterConfigurationValue(v.Key, v.Value)).ToArray(),
    };

    [Fact]
    public void InitialRender_ShowsLoadingIndicator()
    {
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<PrinterStatus>().Task);

        var cut = Render<PrinterInfo>();

        Assert.NotNull(cut.Find("[data-testid='printer-info-loading']"));
    }

    [Fact]
    public void WhenLoaded_ShowsGeneralSectionFromStatus()
    {
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(StatusWith(("device.friendly_name", "Warehouse-01")));

        var cut = Render<PrinterInfo>();

        cut.WaitForAssertion(() =>
        {
            var generalSection = cut.FindAll("[data-testid='printer-info-section']")[0];
            Assert.Contains("Warehouse-01", generalSection.TextContent);
            Assert.Contains("7.6.2", generalSection.TextContent);
            Assert.Contains("V93.21.49Z", generalSection.TextContent);
            Assert.Contains("12345", generalSection.TextContent);
        });
    }

    [Fact]
    public void WhenLoaded_ShowsConnectivitySectionFromStatus()
    {
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(StatusWith(
                ("wlan.essid", "Warehouse-WiFi"),
                ("wlan.security", "wpa psk"),
                ("wlan.state", "CONNECTED"),
                ("wlan.ip.addr", "192.168.1.50"),
                ("wlan.ip.netmask", "255.255.255.0"),
                ("wlan.ip.gateway", "192.168.1.1"),
                ("wlan.ip.protocol", "permanent"),
                ("ip.dhcp.enable", "off")));

        var cut = Render<PrinterInfo>();

        cut.WaitForAssertion(() =>
        {
            var connectivitySection = cut.FindAll("[data-testid='printer-info-section']")[1];
            Assert.Contains("Warehouse-WiFi", connectivitySection.TextContent);
            Assert.Contains("wpa psk", connectivitySection.TextContent);
            Assert.Contains("192.168.1.50", connectivitySection.TextContent);
            Assert.Contains("255.255.255.0", connectivitySection.TextContent);
            Assert.Contains("192.168.1.1", connectivitySection.TextContent);
            Assert.Contains("AABBCCDDEEFF", connectivitySection.TextContent);
        });
    }

    [Fact]
    public void WhenAFieldIsNotAvailable_ShowsUnknownRatherThanBlank()
    {
        // No configuration values stubbed at all - every General/Connectivity row falls back to
        // "Unknown" rather than rendering blank, so a missing value stays visibly traceable rather
        // than looking like a rendering bug.
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new PrinterStatus
            {
                VersionResult = new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate },
                WebInterfaceState = new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true },
                ConfigurationValues = Array.Empty<PrinterConfigurationValue>(),
            });

        var cut = Render<PrinterInfo>();

        cut.WaitForAssertion(() => Assert.Contains("Unknown", cut.FindAll("[data-testid='printer-info-section']")[0].TextContent));
    }

    [Fact]
    public void DoesNotShowOutOfScopeFields()
    {
        // Odometers, live Darkness/Speed, live Media Width/Length, and Bluetooth min-security-mode/
        // bonding/reconnect/controller-mode are explicitly out of scope - this app has no SGD read
        // for any of them today (see PrinterInfo.razor's own doc comment) - confirms they're genuinely
        // absent, not silently blank rows.
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(StatusWith(("device.friendly_name", "Warehouse-01")));

        var cut = Render<PrinterInfo>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='printer-info-section']")));
        Assert.DoesNotContain("Odometer", cut.Markup);
        Assert.DoesNotContain("Darkness", cut.Markup);
        Assert.DoesNotContain("Bonding", cut.Markup);
    }

    [Fact]
    public void WhenStatusReadFails_ShowsError()
    {
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PrinterStatus>(new InvalidOperationException("simulated read failure")));

        var cut = Render<PrinterInfo>();

        cut.WaitForAssertion(() => Assert.Contains("simulated read failure", cut.Find("[data-testid='printer-info-error']").TextContent));
    }

    [Fact]
    public void ClickingBack_NavigatesToDashboard()
    {
        _statusReader.ReadStatusAsync(Device, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(StatusWith(("device.friendly_name", "Warehouse-01")));
        var cut = Render<PrinterInfo>();

        cut.Find("[data-testid='printer-info-back-button']").Click();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/dashboard", navigation.Uri);
    }

    [Fact]
    public void WhenNoDeviceInSession_RedirectsToPairing()
    {
        _session.Device = null;

        Render<PrinterInfo>();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
    }
}
