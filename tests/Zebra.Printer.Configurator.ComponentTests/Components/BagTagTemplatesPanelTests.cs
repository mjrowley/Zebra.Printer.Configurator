using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class BagTagTemplatesPanelTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IBagTagTemplateService _templateService = Substitute.For<IBagTagTemplateService>();

    public BagTagTemplatesPanelTests()
    {
        Services.AddSingleton(_templateService);
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
    }

    [Fact]
    public void InitialRender_ShowsButtonOnly()
    {
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        Assert.NotNull(cut.Find("[data-testid='deploy-templates-button']"));
        Assert.Empty(cut.FindAll("[data-testid='deploy-templates-confirm-dialog']"));
    }

    [Fact]
    public async Task ClickingButton_WhenNoExistingTemplates_DeploysDirectlyWithoutConfirming()
    {
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        cut.Find("[data-testid='deploy-templates-button']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-complete']")));
        Assert.Empty(cut.FindAll("[data-testid='deploy-templates-confirm-dialog']"));
        await _templateService.Received(1).DeployTemplatesAsync(Device, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingButton_WhenTemplatesAlreadyExist_ShowsConfirmDialogListingThem_AndDoesNotDeployYet()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new[] { "FetchCCT.ZPL", "FetchFDT.ZPL" });
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        cut.Find("[data-testid='deploy-templates-button']").Click();

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='deploy-templates-confirm-message']").TextContent;
            Assert.Contains("FetchCCT.ZPL, FetchFDT.ZPL", text);
        });
        _templateService.DidNotReceive().DeployTemplatesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingOverwrite_DeploysAndShowsCompletion()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new[] { "FetchCCT.ZPL" });
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));
        cut.Find("[data-testid='deploy-templates-button']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-confirm-dialog']")));

        cut.Find("[data-testid='deploy-templates-confirm']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-complete']")));
        await _templateService.Received(1).DeployTemplatesAsync(Device, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CancellingOverwriteConfirmation_ReturnsToIdle_AndDoesNotDeploy()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new[] { "FetchCCT.ZPL" });
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));
        cut.Find("[data-testid='deploy-templates-button']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-confirm-dialog']")));

        cut.Find("[data-testid='deploy-templates-cancel']").Click();

        Assert.Empty(cut.FindAll("[data-testid='deploy-templates-confirm-dialog']"));
        Assert.NotNull(cut.Find("[data-testid='deploy-templates-button']"));
        _templateService.DidNotReceive().DeployTemplatesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WhenCheckFails_ShowsErrorAndDoesNotDeploy()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("simulated check failure")));
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        cut.Find("[data-testid='deploy-templates-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("simulated check failure", cut.Find("[data-testid='deploy-templates-error']").TextContent));
        _templateService.DidNotReceive().DeployTemplatesAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WhenDeployFails_ShowsError()
    {
        _templateService.DeployTemplatesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated deploy failure")));
        var cut = Render<BagTagTemplatesPanel>(p => p.Add(c => c.Device, Device));

        cut.Find("[data-testid='deploy-templates-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("simulated deploy failure", cut.Find("[data-testid='deploy-templates-error']").TextContent));
    }

    [Fact]
    public void ClickingButton_RaisesIsActiveChangedTrue_ThenFalseOnceComplete()
    {
        var activeStates = new List<bool>();
        var cut = Render<BagTagTemplatesPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));

        cut.Find("[data-testid='deploy-templates-button']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-complete']")));
        Assert.Equal([true, false], activeStates);
    }

    [Fact]
    public void ConfirmingDialog_StaysActiveThroughoutThenBecomesInactiveOnCancel()
    {
        _templateService.GetExistingTemplateFileNamesAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new[] { "FetchCCT.ZPL" });
        var activeStates = new List<bool>();
        var cut = Render<BagTagTemplatesPanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));
        cut.Find("[data-testid='deploy-templates-button']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='deploy-templates-confirm-dialog']")));

        cut.Find("[data-testid='deploy-templates-cancel']").Click();

        // Only one "became active" transition (Idle -> Checking) - Confirming is still non-idle, so
        // no further IsActiveChanged events fire until Cancel returns to Idle.
        Assert.Equal([true, false], activeStates);
    }
}
