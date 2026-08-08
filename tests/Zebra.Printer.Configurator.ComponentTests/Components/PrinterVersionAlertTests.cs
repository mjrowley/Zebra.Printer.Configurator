using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class PrinterVersionAlertTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private static readonly FirmwareBundle Bundle = new()
    {
        ModelName = "ZD421",
        ExpectedLinkOsVersion = new LinkOsVersion(7, 6, 2),
        ExpectedFirmwareVersion = "V93.21.49Z",
        FirmwareAssetLogicalPath = "ZD421_Firmware/V93.21.49Z.zpl",
    };

    private readonly IPrinterVersionCheckService _versionCheckService = Substitute.For<IPrinterVersionCheckService>();
    private readonly IPrinterFirmwareUpdateService _firmwareUpdateService = Substitute.For<IPrinterFirmwareUpdateService>();
    private readonly IPrinterConnectionModeProvider _connectionModeProvider = new PrinterConnectionModeProvider();
    private readonly PrinterConnectivityMonitor _connectivityMonitor = new();

    public PrinterVersionAlertTests()
    {
        Services.AddSingleton(_versionCheckService);
        Services.AddSingleton(_firmwareUpdateService);
        Services.AddSingleton(_connectionModeProvider);
        Services.AddSingleton(_connectivityMonitor);
    }

    private IRenderedComponent<PrinterVersionAlert> RenderAlert(string? wifiIpAddress = null, bool wifiConnected = false)
    {
        if (wifiConnected)
        {
            _connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        }

        return Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.WifiIpAddress, wifiIpAddress));
    }

    [Fact]
    public void UpToDate_RendersNothingAndIsNotBlocking()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate, Bundle = Bundle });
        var blockingValues = new List<bool>();
        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.BlockingChanged, EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b))));

        cut.WaitForAssertion(() => Assert.Contains(false, blockingValues));
        Assert.Empty(cut.FindAll("[data-testid='version-check-newer']"));
        Assert.Empty(cut.FindAll("[data-testid='version-check-needs-update']"));
        Assert.Empty(cut.FindAll("[data-testid='version-check-unsupported']"));
    }

    [Fact]
    public void NewerThanExpected_ShowsExactMessage_AndIsNotBlocking()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult
            {
                Outcome = PrinterVersionOutcome.NewerThanExpected,
                Bundle = Bundle,
                LinkOsVersionFound = "7.7.0",
                FirmwareVersionFound = "V93.22.01Z",
            });
        var blockingValues = new List<bool>();
        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.BlockingChanged, EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b))));

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='version-check-newer']").TextContent;
            Assert.Equal(
                "Connected printer has a higher firmware version than expected. Expected Link-OS version: 7.6.2 - printer version: 7.7.0. Expected firmware version: V93.21.49Z - printer version: V93.22.01Z",
                text);
        });
        Assert.Contains(false, blockingValues);
    }

    [Fact]
    public void NeedsUpdate_ShowsExactMessage_AndBlocks()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult
            {
                Outcome = PrinterVersionOutcome.NeedsUpdate,
                Bundle = Bundle,
                LinkOsVersionFound = "7.5.0",
                FirmwareVersionFound = "V93.21.06Z",
            });
        var blockingValues = new List<bool>();
        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.BlockingChanged, EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b))));

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='version-check-needs-update']").TextContent;
            Assert.Equal(
                "Connected printer requires a firmware update. Press 'Update Firmware' to update. Expected Link-OS version: 7.6.2 - printer version: 7.5.0. Expected firmware version: V93.21.49Z - printer version: V93.21.06Z",
                text);
        });
        Assert.Contains(true, blockingValues);
        Assert.NotNull(cut.Find("[data-testid='update-firmware-button']"));
    }

    [Fact]
    public void NeedsUpdate_WhenWifiNotConnected_UpdateFirmwareButtonIsDisabled()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = Bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" });
        var cut = RenderAlert(wifiIpAddress: null, wifiConnected: false);

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void NeedsUpdate_WhenWifiConnected_UpdateFirmwareButtonIsEnabled()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = Bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" });
        var cut = RenderAlert(wifiIpAddress: "192.168.1.50", wifiConnected: true);

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void ClickingUpdateFirmware_CallsUpdateService_AndReChecksAfterSuccess()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(
                new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = Bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" },
                new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate, Bundle = Bundle, LinkOsVersionFound = "7.6.2", FirmwareVersionFound = "V93.21.49Z" });
        _firmwareUpdateService.UpdateFirmwareAsync(Device, Bundle, "192.168.1.50", Arg.Any<IProgress<FirmwareUpdateProgress>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var blockingValues = new List<bool>();
        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.WifiIpAddress, "192.168.1.50")
            .Add(c => c.BlockingChanged, EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b))));
        _connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='update-firmware-button']").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='version-check-needs-update']")));
        _ = _firmwareUpdateService.Received(1).UpdateFirmwareAsync(Device, Bundle, "192.168.1.50", Arg.Any<IProgress<FirmwareUpdateProgress>>(), Arg.Any<CancellationToken>());
        Assert.Equal(PrinterConnectionMode.Wifi, _connectionModeProvider.Mode);
        Assert.Contains(false, blockingValues);
    }

    [Fact]
    public void ClickingUpdateFirmware_WhenUpdateFails_ShowsErrorAndStaysBlocking()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = Bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" });
        _firmwareUpdateService.UpdateFirmwareAsync(Device, Bundle, "192.168.1.50", Arg.Any<IProgress<FirmwareUpdateProgress>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated update failure")));
        var cut = RenderAlert(wifiIpAddress: "192.168.1.50", wifiConnected: true);
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='update-firmware-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("simulated update failure", cut.Find("[data-testid='update-firmware-error']").TextContent));
        Assert.NotNull(cut.Find("[data-testid='version-check-needs-update']"));
    }

    [Fact]
    public void Unsupported_ShowsExactMessage_AndBlocks()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.Unsupported });
        var blockingValues = new List<bool>();
        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.BlockingChanged, EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b))));

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='version-check-unsupported']").TextContent;
            Assert.Equal(
                "Connected printer is an unsupported model for firmware update. Press 'Skip' to continue to printer configuration or 'Cancel' to connect to a different printer.",
                text);
        });
        Assert.Contains(true, blockingValues);
    }

    [Fact]
    public void Unsupported_ClickingSkip_UnblocksAndKeepsShowingMessage()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.Unsupported });
        var blockingValues = new List<bool>();
        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.BlockingChanged, EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b))));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-skip']")));

        cut.Find("[data-testid='version-check-skip']").Click();

        Assert.True(blockingValues[0]);
        Assert.False(blockingValues[^1]);
    }

    [Fact]
    public void Unsupported_ClickingCancel_RaisesOnCancelled()
    {
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.Unsupported });
        var cancelled = false;
        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.OnCancelled, EventCallback.Factory.Create(this, () => cancelled = true)));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-cancel']")));

        cut.Find("[data-testid='version-check-cancel']").Click();

        Assert.True(cancelled);
    }
}
