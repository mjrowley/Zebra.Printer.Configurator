using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UnitTests.Firmware;

public class FirmwareUpdateStatusMonitorTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        var monitor = new FirmwareUpdateStatusMonitor();

        Assert.Equal(FirmwareUpdateRunState.Idle, monitor.State);
        Assert.Null(monitor.Progress);
        Assert.Null(monitor.ErrorMessage);
    }

    [Fact]
    public void SetRunning_SetsStateAndRaisesChanged_ClearingPreviousProgressAndError()
    {
        var monitor = new FirmwareUpdateStatusMonitor();
        monitor.SetFailed("previous failure");
        var raised = 0;
        monitor.Changed += (_, _) => raised++;

        monitor.SetRunning();

        Assert.Equal(FirmwareUpdateRunState.Running, monitor.State);
        Assert.Null(monitor.Progress);
        Assert.Null(monitor.ErrorMessage);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetProgress_UpdatesProgressAndRaisesChanged()
    {
        var monitor = new FirmwareUpdateStatusMonitor();
        monitor.SetRunning();
        var raised = 0;
        monitor.Changed += (_, _) => raised++;
        var progress = new FirmwareUpdateProgress { Stage = FirmwareUpdateStage.Downloading, BytesWritten = 100, TotalBytes = 200 };

        monitor.SetProgress(progress);

        Assert.Same(progress, monitor.Progress);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetSucceeded_SetsStateAndRaisesChanged()
    {
        var monitor = new FirmwareUpdateStatusMonitor();
        monitor.SetRunning();
        var raised = 0;
        monitor.Changed += (_, _) => raised++;

        monitor.SetSucceeded();

        Assert.Equal(FirmwareUpdateRunState.Succeeded, monitor.State);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetFailed_SetsStateAndErrorMessageAndRaisesChanged()
    {
        var monitor = new FirmwareUpdateStatusMonitor();
        monitor.SetRunning();
        var raised = 0;
        monitor.Changed += (_, _) => raised++;

        monitor.SetFailed("simulated failure");

        Assert.Equal(FirmwareUpdateRunState.Failed, monitor.State);
        Assert.Equal("simulated failure", monitor.ErrorMessage);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Reset_ReturnsToIdle_ClearingProgressAndError()
    {
        var monitor = new FirmwareUpdateStatusMonitor();
        monitor.SetFailed("simulated failure");
        var raised = 0;
        monitor.Changed += (_, _) => raised++;

        monitor.Reset();

        Assert.Equal(FirmwareUpdateRunState.Idle, monitor.State);
        Assert.Null(monitor.Progress);
        Assert.Null(monitor.ErrorMessage);
        Assert.Equal(1, raised);
    }
}
