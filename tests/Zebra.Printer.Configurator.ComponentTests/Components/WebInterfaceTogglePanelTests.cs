using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class WebInterfaceTogglePanelTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private readonly IWebInterfaceService _webInterfaceService = Substitute.For<IWebInterfaceService>();
    private readonly PrinterActivityMonitor _activityMonitor = new();

    public WebInterfaceTogglePanelTests()
    {
        Services.AddSingleton(_webInterfaceService);
        Services.AddSingleton(_activityMonitor);
    }

    [Fact]
    public void WhenBothEnabled_ShowsDisableButton()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });

        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));

        cut.WaitForAssertion(() => Assert.Equal("Disable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim()));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void WhenEitherDisabled_ShowsEnableButton(bool httpsEnabled, bool httpEnabled)
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = httpsEnabled, HttpEnabled = httpEnabled });

        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));

        cut.WaitForAssertion(() => Assert.Equal("Enable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim()));
    }

    [Fact]
    public async Task ClickingEnable_SetsBothKeysOn_AndShowsRestartPrompt()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='web-interface-restart-confirm-dialog']")));
        await _webInterfaceService.Received(1).SetEnabledAsync(Device, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingDisable_SetsBothKeysOff()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='web-interface-restart-confirm-dialog']")));
        await _webInterfaceService.Received(1).SetEnabledAsync(Device, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingNo_ShowsRequiresRestartButton_ClickingItReopensPromptWithoutReapplyingToggle()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));

        cut.Find("[data-testid='web-interface-restart-confirm-no']").Click();

        Assert.Equal("Web Interface Change Requires Restart", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='web-interface-restart-confirm-dialog']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        Assert.NotNull(cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));
        // Only the one call from the original toggle - re-opening the prompt must not re-apply it.
        await _webInterfaceService.Received(1).SetEnabledAsync(Device, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingYes_RestartsPrinter_AndShowsCompletion()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));

        cut.Find("[data-testid='web-interface-restart-confirm-yes']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='web-interface-complete']")));
        await _webInterfaceService.Received(1).RestartPrinterAsync(Device, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingCloseAfterCompletion_RaisesOnFinished()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        var finishedRaised = false;
        var cut = Render<WebInterfaceTogglePanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.OnFinished, () => finishedRaised = true));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));
        cut.Find("[data-testid='web-interface-restart-confirm-yes']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-complete']"));

        cut.Find("button").Click();

        Assert.True(finishedRaised);
    }

    [Fact]
    public void WhenInitialReadFails_ShowsError_AndTryAgainReReadsState()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<WebInterfaceState>(new InvalidOperationException("simulated read failure")),
                Task.FromResult(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true }));

        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));

        cut.WaitForAssertion(() => Assert.Contains("simulated read failure", cut.Find("[data-testid='web-interface-error']").TextContent));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("Disable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim()));
    }

    [Fact]
    public void WhenToggleFails_ShowsError()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        _webInterfaceService.SetEnabledAsync(Device, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated toggle failure")));
        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("simulated toggle failure", cut.Find("[data-testid='web-interface-error']").TextContent));
    }

    [Fact]
    public void WhenRestartFails_ShowsError()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        _webInterfaceService.RestartPrinterAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated restart failure")));
        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));

        cut.Find("[data-testid='web-interface-restart-confirm-yes']").Click();

        cut.WaitForAssertion(() => Assert.Contains("simulated restart failure", cut.Find("[data-testid='web-interface-error']").TextContent));
    }

    [Fact]
    public void WhileLoading_MarksActivityMonitorBusy()
    {
        var readTcs = new TaskCompletionSource<WebInterfaceState>();
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>()).Returns(readTcs.Task);

        Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));

        Assert.True(_activityMonitor.IsBusy);

        readTcs.SetResult(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
    }

    [Fact]
    public void ClickingToggle_MarksActivityMonitorBusyUntilConfirmPrompt()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        var setEnabledTcs = new TaskCompletionSource();
        _webInterfaceService.SetEnabledAsync(Device, true, Arg.Any<CancellationToken>()).Returns(setEnabledTcs.Task);
        var cut = Render<WebInterfaceTogglePanel>(p => p.Add(c => c.Device, Device));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        Assert.True(_activityMonitor.IsBusy);

        setEnabledTcs.SetResult();

        cut.WaitForAssertion(() => Assert.False(_activityMonitor.IsBusy));
    }

    [Fact]
    public void IsActiveChanged_RaisedTrueThenFalseAcrossLoad()
    {
        var activeStates = new List<bool>();
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });

        var cut = Render<WebInterfaceTogglePanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.IsActiveChanged, active => activeStates.Add(active)));

        cut.WaitForAssertion(() => Assert.Equal([true, false], activeStates));
    }
}
