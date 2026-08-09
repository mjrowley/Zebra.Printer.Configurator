using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;

namespace Zebra.Printer.Configurator.UnitTests.Workflow;

public class PairAndConfigureWorkflowTests
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private static readonly WlanConfiguration Configuration = new()
    {
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    private static (
        IPrinterConfigurationService Configuration,
        IPdfDirectService PdfDirect,
        IPrinterRestartService Restart,
        IPrinterConnectivityTestService ConnectivityTest,
        PairAndConfigureWorkflow Workflow) CreateWorkflow()
    {
        var configurationService = Substitute.For<IPrinterConfigurationService>();
        var pdfDirectService = Substitute.For<IPdfDirectService>();
        var restartService = Substitute.For<IPrinterRestartService>();
        var connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
        var workflow = new PairAndConfigureWorkflow(configurationService, pdfDirectService, restartService, connectivityTestService);

        return (configurationService, pdfDirectService, restartService, connectivityTestService, workflow);
    }

    [Fact]
    public async Task RunAsync_TransitionsThroughExpectedStates_OnSuccess()
    {
        var (_, _, _, connectivityTestService, workflow) = CreateWorkflow();
        connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Succeeded("CONNECTED"));

        var observedStates = new List<PairingWorkflowState>();
        workflow.StateChanged += (_, _) => observedStates.Add(workflow.State);

        await workflow.RunAsync(Device, Configuration);

        Assert.Equal(
        [
            PairingWorkflowState.ApplyingConfiguration,
            PairingWorkflowState.EnablingPdfDirect,
            PairingWorkflowState.Restarting,
            PairingWorkflowState.TestingConnection,
            PairingWorkflowState.Succeeded,
        ], observedStates);
        Assert.Equal(PairingWorkflowState.Succeeded, workflow.State);
        Assert.True(workflow.Result?.Success);
        Assert.Null(workflow.FailureReason);
    }

    [Fact]
    public async Task RunAsync_CallsServicesInOrder()
    {
        var (configurationService, pdfDirectService, restartService, connectivityTestService, workflow) = CreateWorkflow();
        connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Succeeded("CONNECTED"));

        await workflow.RunAsync(Device, Configuration);

        Received.InOrder(() =>
        {
            configurationService.ApplyAsync(Device, Configuration, Arg.Any<CancellationToken>());
            pdfDirectService.EnsureEnabledAsync(Device, Arg.Any<CancellationToken>());
            restartService.RestartAsync(Device, Arg.Any<CancellationToken>());
            connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RunAsync_EndsInFailed_WhenConnectivityTestFails()
    {
        var (_, _, _, connectivityTestService, workflow) = CreateWorkflow();
        connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Failed("Printer did not respond."));

        await workflow.RunAsync(Device, Configuration);

        Assert.Equal(PairingWorkflowState.Failed, workflow.State);
        Assert.Equal("Printer did not respond.", workflow.FailureReason);
        Assert.False(workflow.Result?.Success);
    }

    [Fact]
    public async Task RunAsync_EndsInFailed_WhenApplyThrows()
    {
        var (configurationService, pdfDirectService, restartService, connectivityTestService, workflow) = CreateWorkflow();
        configurationService.ApplyAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Bluetooth connection failed.")));

        await workflow.RunAsync(Device, Configuration);

        Assert.Equal(PairingWorkflowState.Failed, workflow.State);
        Assert.Equal("Bluetooth connection failed.", workflow.FailureReason);
        await pdfDirectService.DidNotReceive().EnsureEnabledAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
        await restartService.DidNotReceive().RestartAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
        await connectivityTestService.DidNotReceive().TestConnectionAsync(Arg.Any<PrinterDevice>(), Arg.Any<WlanConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_EndsInFailed_WhenPdfDirectThrows()
    {
        var (_, pdfDirectService, restartService, connectivityTestService, workflow) = CreateWorkflow();
        pdfDirectService.EnsureEnabledAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("PDF Direct did not enable.")));

        await workflow.RunAsync(Device, Configuration);

        Assert.Equal(PairingWorkflowState.Failed, workflow.State);
        Assert.Equal("PDF Direct did not enable.", workflow.FailureReason);
        await restartService.DidNotReceive().RestartAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
        await connectivityTestService.DidNotReceive().TestConnectionAsync(Arg.Any<PrinterDevice>(), Arg.Any<WlanConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OnCancellation_ResetsStateToNotStarted_AndRethrows()
    {
        var (configurationService, _, restartService, connectivityTestService, workflow) = CreateWorkflow();
        using var cts = new CancellationTokenSource();
        configurationService.ApplyAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAsync<OperationCanceledException>(() => workflow.RunAsync(Device, Configuration, cts.Token));

        Assert.Equal(PairingWorkflowState.NotStarted, workflow.State);
        await restartService.DidNotReceive().RestartAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
        await connectivityTestService.DidNotReceive().TestConnectionAsync(Arg.Any<PrinterDevice>(), Arg.Any<WlanConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ResetsPreviousResultAndFailureReason_OnRetry()
    {
        var (_, _, _, connectivityTestService, workflow) = CreateWorkflow();
        connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Failed("first failure"), ConnectionTestResult.Succeeded("CONNECTED"));

        await workflow.RunAsync(Device, Configuration);
        Assert.Equal(PairingWorkflowState.Failed, workflow.State);

        await workflow.RunAsync(Device, Configuration);

        Assert.Equal(PairingWorkflowState.Succeeded, workflow.State);
        Assert.Null(workflow.FailureReason);
        Assert.True(workflow.Result?.Success);
    }
}
