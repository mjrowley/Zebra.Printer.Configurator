using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class BagTagTemplatesPanelTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IBagTagTemplateService _templateService = Substitute.For<IBagTagTemplateService>();
    private readonly PrinterActivityMonitor _activityMonitor = new();

    public BagTagTemplatesPanelTests()
    {
        Services.AddSingleton(_templateService);
        Services.AddSingleton(_activityMonitor);
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
    }

    [Fact]
    public void InitialRender_ShowsNothing()
    {
        // The trigger now lives in the host page's PrinterActionsMenu overflow menu (RequestDeployAsync,
        // called via @ref) - this component only renders once actually triggered.
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        Assert.Empty(cut.FindAll("[data-testid='deploy-templates-confirm-dialog']"));
    }

    [Fact]
    public async Task RequestDeployAsync_WhenNoExistingTemplates_DeploysDirectlyWithoutConfirming()
    {
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        await cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-complete']")));
        Assert.Empty(cut.FindAll("[data-testid='deploy-templates-confirm-dialog']"));
        await _templateService.Received(1).DeployTemplatesAsync(Device, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestDeployAsync_WhenTemplatesAlreadyExist_ShowsConfirmDialogListingThem_AndDoesNotDeployYet()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new[] { "FetchCCT.ZPL", "FetchFDT.ZPL" });
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        await cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='deploy-templates-confirm-message']").TextContent;
            Assert.Contains("FetchCCT.ZPL, FetchFDT.ZPL", text);
        });
        _ = _templateService.DidNotReceive().DeployTemplatesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingOverwrite_DeploysAndShowsCompletion()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new[] { "FetchCCT.ZPL" });
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-confirm-dialog']")));

        cut.Find("[data-testid='deploy-templates-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-complete']")));
        await _templateService.Received(1).DeployTemplatesAsync(Device, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingOverwriteConfirmation_ReturnsToIdle_AndDoesNotDeploy()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new[] { "FetchCCT.ZPL" });
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));
        await cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-confirm-dialog']")));

        cut.Find("[data-testid='deploy-templates-cancel']").Click();

        Assert.Empty(cut.FindAll("[data-testid='deploy-templates-confirm-dialog']"));
        _ = _templateService.DidNotReceive().DeployTemplatesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCheckFails_ShowsErrorAndDoesNotDeploy()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("simulated check failure")));
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        await cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());

        cut.WaitForAssertion(() => Assert.Contains("simulated check failure", cut.Find("[data-testid='deploy-templates-error']").TextContent));
        _ = _templateService.DidNotReceive().DeployTemplatesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenDeployFails_ShowsError()
    {
        _templateService.DeployTemplatesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated deploy failure")));
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        await cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());

        cut.WaitForAssertion(() => Assert.Contains("simulated deploy failure", cut.Find("[data-testid='deploy-templates-error']").TextContent));
    }

    [Fact]
    public async Task RequestDeployAsync_RaisesIsActiveChangedTrue_ThenFalseOnceComplete()
    {
        var activeStates = new List<bool>();
        var cut = Render<BagTagTemplatesPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));

        await cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-complete']")));
        Assert.Equal([true, false], activeStates);
    }

    [Fact]
    public async Task ConfirmingDialog_StaysActiveThroughoutThenBecomesInactiveOnCancel()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new[] { "FetchCCT.ZPL" });
        var activeStates = new List<bool>();
        var cut = Render<BagTagTemplatesPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));
        await cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-confirm-dialog']")));

        cut.Find("[data-testid='deploy-templates-cancel']").Click();

        // Only one "became active" transition (Idle -> Checking) - Confirming is still non-idle, so
        // no further IsActiveChanged events fire until Cancel returns to Idle.
        Assert.Equal([true, false], activeStates);
    }

    [Fact]
    public void RequestDeployAsync_MarksActivityMonitorBusyUntilComplete()
    {
        // Deliberately NOT awaited - RequestDeployAsync's own call chain (via Deploy()) genuinely
        // hangs at deployTcs until SetResult() below runs, so awaiting it here would deadlock this
        // test against itself. cut.InvokeAsync still dispatches it onto the renderer's own
        // synchronization context (matching how a real @onclick dispatch would), it just isn't
        // waited on to fully finish before the test continues - the same way bUnit's own .Click()
        // doesn't block on a handler that's still pending a real async gap.
        var deployTcs = new TaskCompletionSource();
        _templateService.DeployTemplatesAsync(Device, Arg.Any<CancellationToken>()).Returns(deployTcs.Task);
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        _ = cut.InvokeAsync(() => cut.Instance.RequestDeployAsync());

        cut.WaitForAssertion(() => Assert.True(_activityMonitor.IsBusy));

        deployTcs.SetResult();

        cut.WaitForAssertion(() => Assert.False(_activityMonitor.IsBusy));
    }
}
