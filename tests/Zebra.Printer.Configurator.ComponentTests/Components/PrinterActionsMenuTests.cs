using Bunit;
using Microsoft.AspNetCore.Components;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

// bUnit renders Blazor's own output (plain HTML/attributes) without ever executing a real browser's
// JS - so @material/web's actual menu open/close animation, positioning, and "closed" event never
// run here. What IS tested: the Blazor-level contract - the anchor toggles the "open" attribute this
// component tracks, each menu item's @onclick invokes the right callback, and disabled reflects
// correctly.
public class PrinterActionsMenuTests : BunitContext
{
    private IRenderedComponent<PrinterActionsMenu> RenderMenu(
        EventCallback? onRecheckConfiguration = null,
        EventCallback? onPushBagTagTemplates = null,
        EventCallback? onCalibrateMedia = null,
        EventCallback? onFactoryReset = null,
        bool recheckConfigurationDisabled = false)
    {
        return Render<PrinterActionsMenu>(p =>
        {
            p.Add(c => c.AnchorId, "test-actions-menu-anchor");
            p.Add(c => c.OnRecheckConfiguration, onRecheckConfiguration ?? EventCallback.Empty);
            p.Add(c => c.RecheckConfigurationDisabled, recheckConfigurationDisabled);
            p.Add(c => c.OnPushBagTagTemplates, onPushBagTagTemplates ?? EventCallback.Empty);
            p.Add(c => c.OnCalibrateMedia, onCalibrateMedia ?? EventCallback.Empty);
            p.Add(c => c.OnFactoryReset, onFactoryReset ?? EventCallback.Empty);
        });
    }

    [Fact]
    public void InitialRender_MenuIsClosed_AndShowsExpectedItems()
    {
        var cut = RenderMenu();

        Assert.False(cut.Find("[data-testid='printer-actions-menu']").HasAttribute("open"));
        // "fixed" (not the default "absolute") - the anchor lives inside a scrolling container
        // (MainLayout's .function-section), and an "absolute"-positioned popup gets clipped at that
        // container's boundary with no way to scroll to the rest of it once it has enough items.
        Assert.Equal("fixed", cut.Find("[data-testid='printer-actions-menu']").GetAttribute("positioning"));
        Assert.NotNull(cut.Find("[data-testid='menu-item-recheck-configuration']"));
        Assert.NotNull(cut.Find("[data-testid='menu-item-push-bag-tag-templates']"));
        Assert.NotNull(cut.Find("[data-testid='menu-item-calibrate-media']"));
        Assert.NotNull(cut.Find("[data-testid='menu-item-factory-reset']"));
    }

    [Fact]
    public void ClickingAnchor_OpensMenu()
    {
        var cut = RenderMenu();

        cut.Find("[data-testid='printer-actions-menu-button']").Click();

        Assert.True(cut.Find("[data-testid='printer-actions-menu']").HasAttribute("open"));
    }

    [Fact]
    public void ClickingAnchorTwice_ClosesMenuAgain()
    {
        var cut = RenderMenu();
        cut.Find("[data-testid='printer-actions-menu-button']").Click();

        cut.Find("[data-testid='printer-actions-menu-button']").Click();

        Assert.False(cut.Find("[data-testid='printer-actions-menu']").HasAttribute("open"));
    }

    [Fact]
    public void ClickingAMenuItem_ClosesTheMenu()
    {
        // md-menu closes itself on item selection via real @material/web JS this app never executes
        // in tests - Select() below closes it from the Blazor side too (see PrinterActionsMenu's own
        // doc comment on why it can't just rely on md-menu's "closed" event), so this is real,
        // testable behavior rather than something only the untested JS provides.
        var cut = RenderMenu(onCalibrateMedia: EventCallback.Factory.Create(this, () => { }));
        cut.Find("[data-testid='printer-actions-menu-button']").Click();
        Assert.True(cut.Find("[data-testid='printer-actions-menu']").HasAttribute("open"));

        cut.Find("[data-testid='menu-item-calibrate-media']").Click();

        Assert.False(cut.Find("[data-testid='printer-actions-menu']").HasAttribute("open"));
    }

    [Fact]
    public void ClickingRecheckConfigurationItem_InvokesCallback()
    {
        var invoked = false;
        var cut = RenderMenu(onRecheckConfiguration: EventCallback.Factory.Create(this, () => invoked = true));

        cut.Find("[data-testid='menu-item-recheck-configuration']").Click();

        Assert.True(invoked);
    }

    [Fact]
    public void ClickingPushBagTagTemplatesItem_InvokesCallback()
    {
        var invoked = false;
        var cut = RenderMenu(onPushBagTagTemplates: EventCallback.Factory.Create(this, () => invoked = true));

        cut.Find("[data-testid='menu-item-push-bag-tag-templates']").Click();

        Assert.True(invoked);
    }

    [Fact]
    public void ClickingCalibrateMediaItem_InvokesCallback()
    {
        var invoked = false;
        var cut = RenderMenu(onCalibrateMedia: EventCallback.Factory.Create(this, () => invoked = true));

        cut.Find("[data-testid='menu-item-calibrate-media']").Click();

        Assert.True(invoked);
    }

    [Fact]
    public void ClickingFactoryResetItem_InvokesCallback()
    {
        var invoked = false;
        var cut = RenderMenu(onFactoryReset: EventCallback.Factory.Create(this, () => invoked = true));

        cut.Find("[data-testid='menu-item-factory-reset']").Click();

        Assert.True(invoked);
    }

    [Fact]
    public void RecheckConfigurationDisabled_ReflectsOnItem()
    {
        var cut = RenderMenu(recheckConfigurationDisabled: true);

        Assert.True(cut.Find("[data-testid='menu-item-recheck-configuration']").HasAttribute("disabled"));
    }
}
