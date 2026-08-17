using Bunit;
using Microsoft.AspNetCore.Components;
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

    // Mounts with the merged read already "complete" (StatusLoading=false) - this component's
    // initial display now comes from the page's own merged IPrinterStatusReader read rather than
    // its own Bluetooth call, so InitialState/StatusLoading stand in for that here. A null
    // initialState mirrors the merged read failing.
    private IRenderedComponent<WebInterfaceTogglePanel> RenderPanel(
        WebInterfaceState? initialState,
        EventCallback<bool>? isActiveChanged = null,
        EventCallback? onFinished = null)
    {
        return Render<WebInterfaceTogglePanel>(p =>
        {
            p.Add(c => c.Device, Device);
            p.Add(c => c.InitialState, initialState);
            p.Add(c => c.StatusLoading, false);
            if (isActiveChanged is { } iac)
            {
                p.Add(c => c.IsActiveChanged, iac);
            }

            if (onFinished is { } of)
            {
                p.Add(c => c.OnFinished, of);
            }
        });
    }

    [Fact]
    public void WhenBothEnabled_ShowsDisableButton()
    {
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });

        cut.WaitForAssertion(() => Assert.Equal("Disable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim()));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void WhenEitherDisabled_ShowsEnableButton(bool httpsEnabled, bool httpEnabled)
    {
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = httpsEnabled, HttpEnabled = httpEnabled });

        cut.WaitForAssertion(() => Assert.Equal("Enable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim()));
    }

    [Fact]
    public async Task ClickingEnable_SetsBothKeysOn_AndShowsRestartPrompt()
    {
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='web-interface-restart-confirm-dialog']")));
        await _webInterfaceService.Received(1).SetEnabledAsync(Device, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingDisable_SetsBothKeysOff()
    {
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='web-interface-restart-confirm-dialog']")));
        await _webInterfaceService.Received(1).SetEnabledAsync(Device, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingNo_ShowsRequiresRestartButton_ClickingItReopensPromptWithoutReapplyingToggle()
    {
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        Assert.DoesNotContain("btn-error", cut.Find("[data-testid='web-interface-toggle-button']").ClassList);
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));

        cut.Find("[data-testid='web-interface-restart-confirm-no']").Click();

        Assert.Equal("Web Interface Change Requires Restart", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim());
        // Flags the button text red - a plain "Enable/Disable Web Interface" click is reversible, but
        // this state means a toggle was already applied and is silently waiting on a restart to take
        // effect, which is easy to forget about otherwise.
        Assert.Contains("btn-error", cut.Find("[data-testid='web-interface-toggle-button']").ClassList);
        Assert.Empty(cut.FindAll("[data-testid='web-interface-restart-confirm-dialog']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        Assert.NotNull(cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));
        // Only the one call from the original toggle - re-opening the prompt must not re-apply it.
        await _webInterfaceService.Received(1).SetEnabledAsync(Device, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingYes_RestartsPrinter_AndShowsCompletion()
    {
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
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
        // CloseComplete self-heals via a direct WebInterfaceService.ReadStateAsync call (unchanged,
        // outside the merged-read race window), so this is still stubbed here.
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        var finishedRaised = false;
        var cut = RenderPanel(
            new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false },
            onFinished: EventCallback.Factory.Create(this, () => finishedRaised = true));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));
        cut.Find("[data-testid='web-interface-restart-confirm-yes']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-complete']"));

        cut.Find("md-filled-button").Click();

        Assert.True(finishedRaised);
    }

    [Fact]
    public void WhenInitialReadFails_ShowsError_AndTryAgainReReadsState()
    {
        // A merged-read failure now arrives as InitialState=null (the page could not check web
        // interface status at all) rather than a thrown ReadStateAsync task - only the recovery
        // path (Try Again -> Retry() -> LoadStateAsync) still goes through ReadStateAsync directly.
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });

        var cut = RenderPanel(initialState: null);

        cut.WaitForAssertion(() => Assert.Contains("Could not check web interface status.", cut.Find("[data-testid='web-interface-error']").TextContent));

        cut.Find("md-filled-button").Click();

        cut.WaitForAssertion(() => Assert.Equal("Disable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim()));
    }

    [Fact]
    public void WhenToggleFails_ShowsError()
    {
        _webInterfaceService.SetEnabledAsync(Device, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated toggle failure")));
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("simulated toggle failure", cut.Find("[data-testid='web-interface-error']").TextContent));
    }

    [Fact]
    public void WhenRestartFails_ShowsError()
    {
        _webInterfaceService.RestartPrinterAsync(Device, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated restart failure")));
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));

        cut.Find("[data-testid='web-interface-restart-confirm-yes']").Click();

        cut.WaitForAssertion(() => Assert.Contains("simulated restart failure", cut.Find("[data-testid='web-interface-error']").TextContent));
    }

    [Fact]
    public void Retry_MarksActivityMonitorBusy_ThenClearsOnCompletion()
    {
        // The initial merged-status display no longer marks PrinterActivityMonitor itself (the page
        // owns that for the whole merged read now) - only this panel's own follow-up reads (Retry,
        // CloseComplete) still register their own activity token, same as the toggle/restart flows.
        var readTcs = new TaskCompletionSource<WebInterfaceState>();
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>()).Returns(readTcs.Task);
        var cut = RenderPanel(initialState: null);
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-error']"));
        Assert.False(_activityMonitor.IsBusy);

        cut.Find("md-filled-button").Click();

        Assert.True(_activityMonitor.IsBusy);

        readTcs.SetResult(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });

        cut.WaitForAssertion(() => Assert.False(_activityMonitor.IsBusy));
    }

    [Fact]
    public void ClickingToggle_MarksActivityMonitorBusyUntilConfirmPrompt()
    {
        var setEnabledTcs = new TaskCompletionSource();
        _webInterfaceService.SetEnabledAsync(Device, true, Arg.Any<CancellationToken>()).Returns(setEnabledTcs.Task);
        var cut = RenderPanel(new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));

        cut.Find("[data-testid='web-interface-toggle-button']").Click();

        Assert.True(_activityMonitor.IsBusy);

        setEnabledTcs.SetResult();

        cut.WaitForAssertion(() => Assert.False(_activityMonitor.IsBusy));
    }

    [Fact]
    public void IsActiveChanged_RaisedTrueThenFalseAcrossRetry()
    {
        // Mounting starts in the Loading placeholder (a plain field assignment, not a real SetState
        // transition, so it doesn't itself fire IsActiveChanged) and then immediately applies the
        // already-resolved merged result - that Loading->Failed step IS a real SetState transition,
        // so it fires one IsActiveChanged(false) right away. Retry() then does its own independent
        // Failed->Loading->Idle round trip on top of that.
        var activeStates = new List<bool>();
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
        var cut = RenderPanel(
            initialState: null,
            isActiveChanged: EventCallback.Factory.Create<bool>(this, active => activeStates.Add(active)));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-error']"));
        Assert.Equal([false], activeStates);

        cut.Find("md-filled-button").Click();

        cut.WaitForAssertion(() => Assert.Equal([false, true, false], activeStates));
    }

    [Fact]
    public void MergedUpdateArrivingMidConfirmRestart_IsIgnored()
    {
        // A "Recheck Configuration" firing while the user has an unresolved restart decision pending
        // must not yank the dialog out from under them - simulated here by cycling StatusLoading
        // false->true->false (mirroring the page's own RecheckConfigurationAsync) while _state is
        // ConfirmRestart.
        void AddParameters(ComponentParameterCollectionBuilder<WebInterfaceTogglePanel> p)
        {
            p.Add(c => c.Device, Device);
        }

        var cut = Render<WebInterfaceTogglePanel>(p =>
        {
            AddParameters(p);
            p.Add(c => c.InitialState, new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
            p.Add(c => c.StatusLoading, false);
        });
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));

        cut.Render(p =>
        {
            AddParameters(p);
            p.Add(c => c.InitialState, new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false });
            p.Add(c => c.StatusLoading, true);
        });
        cut.Render(p =>
        {
            AddParameters(p);
            p.Add(c => c.InitialState, new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
            p.Add(c => c.StatusLoading, false);
        });

        Assert.NotNull(cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));
    }

    [Fact]
    public void Retry_RaisesCurrentStateChanged_WithFreshlyReadState()
    {
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
        WebInterfaceState? notifiedState = null;
        var cut = Render<WebInterfaceTogglePanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.InitialState, (WebInterfaceState?)null)
            .Add(c => c.StatusLoading, false)
            .Add(c => c.CurrentStateChanged, EventCallback.Factory.Create<WebInterfaceState?>(this, s => notifiedState = s)));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-error']"));

        cut.Find("md-filled-button").Click();

        cut.WaitForAssertion(() => Assert.NotNull(notifiedState));
        Assert.True(notifiedState!.BothEnabled);
    }

    [Fact]
    public void CloseComplete_WaitsForRestartSettlingDelay_BeforeReadingState()
    {
        // Regression guard for a real on-device failure: closing the completion dialog used to
        // immediately try to reconnect, which reliably lost the race against the printer's own
        // reboot (it drops Bluetooth the instant device.reset is sent) and surfaced a raw connection
        // error instead of the actual post-restart state.
        _webInterfaceService.ReadStateAsync(Device, Arg.Any<CancellationToken>())
            .Returns(new WebInterfaceState { HttpsEnabled = true, HttpEnabled = true });
        var cut = Render<WebInterfaceTogglePanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.InitialState, new WebInterfaceState { HttpsEnabled = false, HttpEnabled = false })
            .Add(c => c.StatusLoading, false)
            .Add(c => c.RestartSettlingDelay, TimeSpan.FromMilliseconds(200)));
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-toggle-button']"));
        cut.Find("[data-testid='web-interface-toggle-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-restart-confirm-dialog']"));
        cut.Find("[data-testid='web-interface-restart-confirm-yes']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='web-interface-complete']"));

        cut.Find("md-filled-button").Click();

        // Immediately after Close, the settling delay should still be running - the read must not
        // have fired yet.
        _webInterfaceService.DidNotReceive().ReadStateAsync(Device, Arg.Any<CancellationToken>());

        cut.WaitForAssertion(
            () => Assert.Equal("Disable Web Interface", cut.Find("[data-testid='web-interface-toggle-button']").TextContent.Trim()),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void WhileStatusLoadingIsTrue_DoesNotShowItsOwnLoadingIndicator()
    {
        // The page's own merged-status read is what's driving StatusLoading here - PrinterVersionAlert
        // already shows one "Checking printer configuration..." spinner for that whole read, so this
        // panel deliberately renders nothing rather than a second, redundant spinner.
        var cut = Render<WebInterfaceTogglePanel>(p => p
            .Add(c => c.Device, Device)
            .Add(c => c.InitialState, (WebInterfaceState?)null)
            .Add(c => c.StatusLoading, true));

        Assert.Empty(cut.FindAll("[data-testid='web-interface-loading']"));
    }
}
