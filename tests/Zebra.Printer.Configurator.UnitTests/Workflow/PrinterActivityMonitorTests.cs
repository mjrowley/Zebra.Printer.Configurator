using Zebra.Printer.Configurator.Core.Workflow;

namespace Zebra.Printer.Configurator.UnitTests.Workflow;

public class PrinterActivityMonitorTests
{
    [Fact]
    public void InitialState_IsNotBusy()
    {
        var monitor = new PrinterActivityMonitor();

        Assert.False(monitor.IsBusy);
    }

    [Fact]
    public void Begin_MarksBusy_AndRaisesChanged()
    {
        var monitor = new PrinterActivityMonitor();
        var changedCount = 0;
        monitor.Changed += (_, _) => changedCount++;

        monitor.Begin("Factory Reset");

        Assert.True(monitor.IsBusy);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void DisposingRegistration_ClearsBusy_AndRaisesChanged()
    {
        var monitor = new PrinterActivityMonitor();
        var registration = monitor.Begin("Factory Reset");
        var changedCount = 0;
        monitor.Changed += (_, _) => changedCount++;

        registration.Dispose();

        Assert.False(monitor.IsBusy);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void MultipleSources_StayBusyUntilAllDisposed()
    {
        var monitor = new PrinterActivityMonitor();
        var first = monitor.Begin("Factory Reset");
        var second = monitor.Begin("Check Configuration");

        first.Dispose();

        Assert.True(monitor.IsBusy);

        second.Dispose();

        Assert.False(monitor.IsBusy);
    }

    [Fact]
    public void DisposingTwice_IsHarmless_AndDoesNotDoubleRaiseChanged()
    {
        var monitor = new PrinterActivityMonitor();
        var registration = monitor.Begin("Factory Reset");
        var changedCount = 0;
        monitor.Changed += (_, _) => changedCount++;

        registration.Dispose();
        registration.Dispose();

        Assert.False(monitor.IsBusy);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void ActiveSources_ReflectsCurrentlyTrackedSources()
    {
        var monitor = new PrinterActivityMonitor();
        monitor.Begin("Factory Reset");
        monitor.Begin("Check Configuration");

        Assert.Equal(["Check Configuration", "Factory Reset"], monitor.ActiveSources.Order());
    }
}
