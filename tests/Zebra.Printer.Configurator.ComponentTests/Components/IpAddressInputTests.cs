using Bunit;
using Microsoft.AspNetCore.Components;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class IpAddressInputTests : BunitContext
{
    public IpAddressInputTests()
    {
        // FocusAsync() on auto-advance goes through JS interop, which isn't configured for these
        // tests - Loose mode lets unconfigured calls no-op instead of throwing.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private (IRenderedComponent<IpAddressInput> Cut, Func<string> GetValue) RenderWithCapturedValue(string initialValue = "")
    {
        var current = initialValue;
        var cut = Render<IpAddressInput>(parameters => parameters
            .Add(p => p.Value, current)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, v => current = v)));
        return (cut, () => current);
    }

    [Fact]
    public void TypingDigitsInFirstOctet_RaisesValueChangedWithCombinedValue()
    {
        var (cut, getValue) = RenderWithCapturedValue();

        cut.Find("#ip-octet-1").Input("192");

        Assert.Equal("192...", getValue());
    }

    [Fact]
    public void TypingNonDigitCharacters_AreFiltered()
    {
        var (cut, getValue) = RenderWithCapturedValue();

        cut.Find("#ip-octet-1").Input("1a9b2");

        Assert.Equal("192...", getValue());
    }

    [Fact]
    public void TypingValueOver255_ClampsTo255()
    {
        var (cut, getValue) = RenderWithCapturedValue();

        cut.Find("#ip-octet-1").Input("999");

        Assert.Equal("255...", getValue());
    }

    [Fact]
    public void TypingMoreThanThreeDigits_IsTruncated()
    {
        var (cut, getValue) = RenderWithCapturedValue();

        cut.Find("#ip-octet-1").Input("1234");

        Assert.Equal("123...", getValue());
    }

    [Fact]
    public void FillingAllFourOctets_ProducesFullDottedQuad()
    {
        var (cut, getValue) = RenderWithCapturedValue();

        cut.Find("#ip-octet-1").Input("192");
        cut.Find("#ip-octet-2").Input("168");
        cut.Find("#ip-octet-3").Input("1");
        cut.Find("#ip-octet-4").Input("50");

        Assert.Equal("192.168.1.50", getValue());
    }

    [Fact]
    public void InitialValue_IsSplitAcrossTheFourOctets()
    {
        var cut = Render<IpAddressInput>(parameters => parameters
            .Add(p => p.Value, "192.168.1.50")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.Equal("192", cut.Find("#ip-octet-1").GetAttribute("value"));
        Assert.Equal("168", cut.Find("#ip-octet-2").GetAttribute("value"));
        Assert.Equal("1", cut.Find("#ip-octet-3").GetAttribute("value"));
        Assert.Equal("50", cut.Find("#ip-octet-4").GetAttribute("value"));
    }
}
