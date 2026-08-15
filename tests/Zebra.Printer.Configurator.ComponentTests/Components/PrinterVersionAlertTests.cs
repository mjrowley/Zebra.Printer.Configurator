using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
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
    private readonly IFirmwareUpdateLauncher _firmwareUpdateLauncher = Substitute.For<IFirmwareUpdateLauncher>();
    private readonly FirmwareUpdateStatusMonitor _updateStatusMonitor = new();
    private readonly IPrinterConnectionModeProvider _connectionModeProvider = new PrinterConnectionModeProvider();
    private readonly PrinterConnectivityMonitor _connectivityMonitor = new();
    private readonly PrinterActivityMonitor _activityMonitor = new();

    public PrinterVersionAlertTests()
    {
        Services.AddSingleton(_versionCheckService);
        Services.AddSingleton(_firmwareUpdateLauncher);
        Services.AddSingleton(_updateStatusMonitor);
        Services.AddSingleton(_connectionModeProvider);
        Services.AddSingleton(_connectivityMonitor);
        Services.AddSingleton(_activityMonitor);
    }

    // Mounts with StatusLoading=true (matching how Pairing.razor/Result.razor always sequence
    // things - the loading flag is already true before the render that first mounts this
    // component), then transitions StatusLoading true->false with the given result, mirroring the
    // page's own merged IPrinterStatusReader read completing. All parameters are re-specified on
    // the second render because bUnit's SetParametersAndRender resets any parameter not included
    // in the builder back to its default.
    private IRenderedComponent<PrinterVersionAlert> RenderAlert(
        PrinterVersionCheckResult? initialResult,
        string? wifiIpAddress = "192.168.1.50",
        bool wifiConnected = true,
        EventCallback<bool>? blockingChanged = null,
        EventCallback? onCancelled = null)
    {
        if (wifiConnected)
        {
            _connectivityMonitor.SetWifi(ConnectionIndicatorState.Connected);
        }

        void AddParameters(ComponentParameterCollectionBuilder<PrinterVersionAlert> p)
        {
            p.Add(c => c.Device, Device);
            p.Add(c => c.WifiIpAddress, wifiIpAddress);
            if (blockingChanged is { } bc)
            {
                p.Add(c => c.BlockingChanged, bc);
            }

            if (onCancelled is { } oc)
            {
                p.Add(c => c.OnCancelled, oc);
            }
        }

        var cut = Render<PrinterVersionAlert>(p =>
        {
            AddParameters(p);
            p.Add(c => c.StatusLoading, true);
        });

        cut.Render(p =>
        {
            AddParameters(p);
            p.Add(c => c.StatusLoading, false);
            p.Add(c => c.VersionResult, initialResult);
        });

        return cut;
    }

    [Fact]
    public void UpToDate_ShowsConfirmationMessage_AndIsNotBlocking()
    {
        var blockingValues = new List<bool>();
        var cut = RenderAlert(
            new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate, Bundle = Bundle },
            blockingChanged: EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b)));

        cut.WaitForAssertion(() => Assert.Contains(false, blockingValues));
        cut.WaitForAssertion(() => Assert.Equal("Printer firmware is up to date.", cut.Find("[data-testid='version-check-up-to-date']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='version-check-newer']"));
        Assert.Empty(cut.FindAll("[data-testid='version-check-needs-update']"));
        Assert.Empty(cut.FindAll("[data-testid='version-check-unsupported']"));
    }

    [Fact]
    public void NewerThanExpected_ShowsExactMessage_AndIsNotBlocking()
    {
        var blockingValues = new List<bool>();
        var cut = RenderAlert(
            new PrinterVersionCheckResult
            {
                Outcome = PrinterVersionOutcome.NewerThanExpected,
                Bundle = Bundle,
                LinkOsVersionFound = "7.7.0",
                FirmwareVersionFound = "V93.22.01Z",
            },
            blockingChanged: EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b)));

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
    public void NeedsUpdate_WhenWifiAvailable_ShowsExactMessage_AndBlocks()
    {
        var blockingValues = new List<bool>();
        var cut = RenderAlert(
            new PrinterVersionCheckResult
            {
                Outcome = PrinterVersionOutcome.NeedsUpdate,
                Bundle = Bundle,
                LinkOsVersionFound = "7.5.0",
                FirmwareVersionFound = "V93.21.06Z",
            },
            blockingChanged: EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b)));

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
    public void NeedsUpdate_ClickingSkip_UnblocksAndKeepsShowingUpdateOption()
    {
        var blockingValues = new List<bool>();
        var cut = RenderAlert(
            new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = Bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" },
            blockingChanged: EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b)));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-skip']")));

        cut.Find("[data-testid='version-check-skip']").Click();

        Assert.True(blockingValues[0]);
        Assert.False(blockingValues[^1]);
        Assert.NotNull(cut.Find("[data-testid='update-firmware-button']"));
    }

    [Fact]
    public void WhenWifiNotAvailable_SkipsVersionCheck_ShowsNothing_AndDoesNotBlock()
    {
        // A never-configured printer has no WiFi yet, and "Configure Printer" is exactly what gives
        // it one - a firmware update can only ever be performed over WiFi, so there's no point running
        // the check (regardless of what outcome it would report) for something the user couldn't act
        // on yet anyway. Result.razor re-renders this component once the printer actually has WiFi,
        // which is when the check (and any update offer) actually happens.
        var blockingValues = new List<bool>();
        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.BlockingChanged, EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b))));

        cut.WaitForAssertion(() => Assert.DoesNotContain(true, blockingValues));
        _ = _versionCheckService.DidNotReceive().CheckAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("[data-testid='version-check-needs-update']"));
        Assert.Empty(cut.FindAll("[data-testid='update-firmware-button']"));
    }

    [Fact]
    public void NeedsUpdate_WhenWifiConnected_UpdateFirmwareButtonIsEnabled()
    {
        var cut = RenderAlert(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = Bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" });

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void ClickingUpdateFirmware_StartsLauncher_ShowsProgress_AndReChecksAfterSuccess()
    {
        // Only the post-success re-check still goes through IPrinterVersionCheckService directly
        // (HandleUpdateStatusChanged, unchanged/untouched by the merged-read change) - the initial
        // result now arrives via the VersionResult parameter instead.
        _versionCheckService.CheckAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.UpToDate, Bundle = Bundle, LinkOsVersionFound = "7.6.2", FirmwareVersionFound = "V93.21.49Z" });
        _firmwareUpdateLauncher.StartAsync(Device, Bundle, "192.168.1.50", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var blockingValues = new List<bool>();
        var cut = RenderAlert(
            new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = Bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" },
            blockingChanged: EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='update-firmware-button']").Click();

        // Clicking optimistically switches to the progress view and hides the button immediately -
        // the actual completion is driven by FirmwareUpdateStatusMonitor, not this click's own Task.
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='update-firmware-progress']"));
            Assert.Empty(cut.FindAll("[data-testid='update-firmware-button']"));
        });
        _ = _firmwareUpdateLauncher.Received(1).StartAsync(Device, Bundle, "192.168.1.50", Arg.Any<CancellationToken>());
        Assert.Equal(PrinterConnectionMode.Wifi, _connectionModeProvider.Mode);

        _updateStatusMonitor.SetRunning();
        _updateStatusMonitor.SetProgress(new FirmwareUpdateProgress { Stage = FirmwareUpdateStage.Downloading, BytesWritten = 50, TotalBytes = 100 });
        cut.WaitForAssertion(() => Assert.Contains("50%", cut.Find("[data-testid='update-firmware-progress']").TextContent));

        _updateStatusMonitor.SetSucceeded();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='update-firmware-progress']"));
            Assert.Empty(cut.FindAll("[data-testid='version-check-needs-update']"));
        });
        Assert.Contains(false, blockingValues);
    }

    [Fact]
    public void ClickingUpdateFirmware_WhenServiceReportsFailure_ShowsErrorAndStaysBlocking()
    {
        // HandleUpdateStatusChanged only re-checks via IPrinterVersionCheckService on a Succeeded
        // outcome, so the Failed path here never touches _versionCheckService at all.
        _firmwareUpdateLauncher.StartAsync(Device, Bundle, "192.168.1.50", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var cut = RenderAlert(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.NeedsUpdate, Bundle = Bundle, LinkOsVersionFound = "7.5.0", FirmwareVersionFound = "V93.21.06Z" });
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='update-firmware-button']").HasAttribute("disabled")));

        cut.Find("[data-testid='update-firmware-button']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='update-firmware-progress']")));

        // The foreground service is what actually reports failure, well after StartAsync returns.
        _updateStatusMonitor.SetFailed("simulated update failure");

        cut.WaitForAssertion(() => Assert.Contains("simulated update failure", cut.Find("[data-testid='update-firmware-error']").TextContent));
        Assert.NotNull(cut.Find("[data-testid='version-check-needs-update']"));
    }

    [Fact]
    public void AlreadyRunningOnMount_SkipsVersionCheck_AndReflectsInProgressState()
    {
        // Simulates the user reopening the app while FirmwareUpdateForegroundService is still
        // transferring - the component must not react to the merged status at all (StatusLoading
        // is left at its default false here), which would otherwise race the service's own
        // connection to the same printer.
        _updateStatusMonitor.SetRunning();
        _updateStatusMonitor.SetProgress(new FirmwareUpdateProgress { Stage = FirmwareUpdateStage.AwaitingReboot });
        var blockingValues = new List<bool>();

        var cut = Render<PrinterVersionAlert>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.WifiIpAddress, "192.168.1.50")
            .Add(c => c.BlockingChanged, EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b))));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='update-firmware-progress']")));
        Assert.Contains("flashing and rebooting", cut.Find("[data-testid='update-firmware-progress']").TextContent);
        _ = _versionCheckService.DidNotReceive().CheckAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
        Assert.Contains(true, blockingValues);
    }

    [Fact]
    public void Unsupported_ShowsExactMessage_AndBlocks()
    {
        var blockingValues = new List<bool>();
        var cut = RenderAlert(
            new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.Unsupported },
            blockingChanged: EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b)));

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
        var blockingValues = new List<bool>();
        var cut = RenderAlert(
            new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.Unsupported },
            blockingChanged: EventCallback.Factory.Create<bool>(this, b => blockingValues.Add(b)));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-skip']")));

        cut.Find("[data-testid='version-check-skip']").Click();

        Assert.True(blockingValues[0]);
        Assert.False(blockingValues[^1]);
    }

    [Fact]
    public void Unsupported_MarksActivityMonitorBusy_ThenClearsOnSkip()
    {
        var cut = RenderAlert(new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.Unsupported });
        cut.WaitForAssertion(() => Assert.True(_activityMonitor.IsBusy));

        cut.Find("[data-testid='version-check-skip']").Click();

        Assert.False(_activityMonitor.IsBusy);
    }

    [Fact]
    public void Unsupported_ClickingCancel_RaisesOnCancelled()
    {
        var cancelled = false;
        var cut = RenderAlert(
            new PrinterVersionCheckResult { Outcome = PrinterVersionOutcome.Unsupported },
            onCancelled: EventCallback.Factory.Create(this, () => cancelled = true));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='version-check-cancel']")));

        cut.Find("[data-testid='version-check-cancel']").Click();

        Assert.True(cancelled);
    }
}
