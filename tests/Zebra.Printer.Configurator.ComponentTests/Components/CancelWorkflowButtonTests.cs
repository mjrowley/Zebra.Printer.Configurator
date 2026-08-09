using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class CancelWorkflowButtonTests : BunitContext
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

    private readonly IPrinterConfigurationService _configurationService = Substitute.For<IPrinterConfigurationService>();
    private readonly IPdfDirectService _pdfDirectService = Substitute.For<IPdfDirectService>();
    private readonly IPrinterRestartService _restartService = Substitute.For<IPrinterRestartService>();
    private readonly IPrinterConnectivityTestService _connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
    private readonly PrinterOperationCancellation _cancellation = new();
    private readonly PairAndConfigureWorkflow _workflow;

    public CancelWorkflowButtonTests()
    {
        _workflow = new PairAndConfigureWorkflow(_configurationService, _pdfDirectService, _restartService, _connectivityTestService);
        Services.AddSingleton(_workflow);
        Services.AddSingleton(_cancellation);
    }

    [Fact]
    public void WhenWorkflowNotRunning_ButtonIsHidden()
    {
        var cut = Render<CancelWorkflowButton>();

        Assert.Empty(cut.FindAll("[data-testid='cancel-workflow-button']"));
    }

    [Fact]
    public void WhenWorkflowRunning_ButtonIsVisible()
    {
        var cut = RenderWithRunningWorkflow();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='cancel-workflow-button']")));
    }

    [Fact]
    public void WhenCancelClicked_ShowsConfirmationDialog()
    {
        var cut = RenderWithRunningWorkflow();
        cut.WaitForAssertion(() => cut.Find("[data-testid='cancel-workflow-button']"));

        cut.Find("[data-testid='cancel-workflow-button']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='cancel-confirm-dialog']"));
    }

    [Fact]
    public void WhenNoReturnClicked_HidesDialogWithoutCancelling()
    {
        var cut = RenderWithRunningWorkflow();
        cut.WaitForAssertion(() => cut.Find("[data-testid='cancel-workflow-button']"));
        cut.Find("[data-testid='cancel-workflow-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='cancel-confirm-dialog']"));

        cut.Find("[data-testid='cancel-confirm-no']").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='cancel-confirm-dialog']")));
        Assert.False(_cancellation.Token.IsCancellationRequested);
    }

    [Fact]
    public void WhenConfirmed_CancelsAndNavigatesToRoot()
    {
        var cut = RenderWithRunningWorkflow();
        cut.WaitForAssertion(() => cut.Find("[data-testid='cancel-workflow-button']"));
        cut.Find("[data-testid='cancel-workflow-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='cancel-confirm-dialog']"));

        cut.Find("[data-testid='cancel-confirm-yes']").Click();

        Assert.True(_cancellation.Token.IsCancellationRequested);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/", navigation.Uri));
    }

    // ApplyAsync never completes, holding the workflow at ApplyingConfiguration - the same
    // interrupted-mid-step scenario Cancel exists for.
    private IRenderedComponent<CancelWorkflowButton> RenderWithRunningWorkflow()
    {
        _configurationService.ApplyAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource().Task);

        var cut = Render<CancelWorkflowButton>();
        _ = _workflow.RunAsync(Device, Configuration, _cancellation.Token);
        return cut;
    }
}
