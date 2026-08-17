using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class CalibrateMediaButtonTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IPrinterCalibrationService _calibrationService = Substitute.For<IPrinterCalibrationService>();
    private readonly PrinterActivityMonitor _activityMonitor = new();

    public CalibrateMediaButtonTests()
    {
        Services.AddSingleton(_calibrationService);
        Services.AddSingleton(_activityMonitor);
    }

    [Fact]
    public void InitialRender_ShowsNothing()
    {
        // The trigger now lives in the host page's PrinterActionsMenu overflow menu (RequestConfirmAsync,
        // called via @ref) - this component only renders once actually triggered.
        var cut = Render<CalibrateMediaButton>(p => p.Add(c => c.Device, Device));

        Assert.Empty(cut.FindAll("[data-testid='calibrate-media-confirm-dialog']"));
    }

    [Fact]
    public async Task RequestConfirmAsync_AlwaysShowsConfirmDialog_AndDoesNotCalibrateYet()
    {
        // Unlike BagTagTemplatesPanel (which only confirms when something would be overwritten),
        // calibration always confirms first - it feeds real labels through the printer every time.
        var cut = Render<CalibrateMediaButton>(p => p.Add(c => c.Device, Device));

        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        Assert.NotNull(cut.Find("[data-testid='calibrate-media-confirm-dialog']"));
        _ = _calibrationService.DidNotReceive().CalibrateAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingCalibration_CalibratesAndShowsCompletion()
    {
        var cut = Render<CalibrateMediaButton>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='calibrate-media-confirm-dialog']")));

        cut.Find("[data-testid='calibrate-media-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='calibrate-media-complete']")));
        Assert.Empty(cut.FindAll("[data-testid='calibrate-media-confirm-dialog']"));
        await _calibrationService.Received(1).CalibrateAsync(Device, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingConfirmation_ReturnsToIdle_AndDoesNotCalibrate()
    {
        var cut = Render<CalibrateMediaButton>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='calibrate-media-confirm-dialog']")));

        cut.Find("[data-testid='calibrate-media-cancel']").Click();

        Assert.Empty(cut.FindAll("[data-testid='calibrate-media-confirm-dialog']"));
        _ = _calibrationService.DidNotReceive().CalibrateAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCalibrationFails_ShowsError()
    {
        _calibrationService.CalibrateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated calibration failure")));
        var cut = Render<CalibrateMediaButton>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='calibrate-media-confirm-dialog']")));

        cut.Find("[data-testid='calibrate-media-confirm']").Click();

        cut.WaitForAssertion(() => Assert.Contains("simulated calibration failure", cut.Find("[data-testid='calibrate-media-error']").TextContent));
    }

    [Fact]
    public async Task RequestConfirmAsyncThenConfirming_RaisesIsActiveChangedTrue_ThenFalseOnceComplete()
    {
        var activeStates = new List<bool>();
        var cut = Render<CalibrateMediaButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));

        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());
        // Confirming already counts as active - it's an unresolved decision the user needs to act on,
        // same as BagTagTemplatesPanel's own overwrite-confirm dialog.
        Assert.Equal([true], activeStates);

        cut.Find("[data-testid='calibrate-media-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='calibrate-media-complete']")));
        Assert.Equal([true, false], activeStates);
    }

    [Fact]
    public async Task CancellingConfirmation_BecomesInactiveAgain()
    {
        var activeStates = new List<bool>();
        var cut = Render<CalibrateMediaButton>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        cut.Find("[data-testid='calibrate-media-cancel']").Click();

        Assert.Equal([true, false], activeStates);
    }

    [Fact]
    public async Task ConfirmingCalibration_MarksActivityMonitorBusyUntilComplete()
    {
        var calibrateTcs = new TaskCompletionSource();
        _calibrationService.CalibrateAsync(Device, Arg.Any<CancellationToken>()).Returns(calibrateTcs.Task);
        var cut = Render<CalibrateMediaButton>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestConfirmAsync());

        cut.Find("[data-testid='calibrate-media-confirm']").Click();

        Assert.True(_activityMonitor.IsBusy);

        calibrateTcs.SetResult();

        cut.WaitForAssertion(() => Assert.False(_activityMonitor.IsBusy));
    }
}
