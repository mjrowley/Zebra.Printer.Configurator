using Bunit;
using Microsoft.AspNetCore.Components;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

// bUnit renders Blazor's own output headlessly, it never executes md-outlined-text-field's real
// Lit/shadow-DOM JS - so these tests assert at the C#/outer-element level (Value/ValueChanged wiring,
// the outer element's own attributes) rather than anything that would need the real browser component
// to actually run. See MdTextField.razor's own doc comment for why it's wired via a plain @oninput
// handler (the same proven pattern IpAddressInputTests.cs already covers) rather than @bind-value
// directly against the custom element.
public class MdTextFieldTests : BunitContext
{
    private (IRenderedComponent<MdTextField> Cut, Func<string> GetValue) RenderWithCapturedValue(string initialValue = "")
    {
        var current = initialValue;
        var cut = Render<MdTextField>(parameters => parameters
            .Add(p => p.Value, current)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, v => current = v)));
        return (cut, () => current);
    }

    [Fact]
    public void TypingIntoField_RaisesValueChangedWithTypedValue()
    {
        var (cut, getValue) = RenderWithCapturedValue();

        cut.Find("md-outlined-text-field").Input("Warehouse-WiFi");

        Assert.Equal("Warehouse-WiFi", getValue());
    }

    [Fact]
    public void InitialValue_IsReflectedOnTheElement()
    {
        var cut = Render<MdTextField>(parameters => parameters
            .Add(p => p.Value, "ZD421")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.Equal("ZD421", cut.Find("md-outlined-text-field").GetAttribute("value"));
    }

    [Fact]
    public void Id_IsSetOnTheOutlinedTextFieldElement()
    {
        var cut = Render<MdTextField>(parameters => parameters
            .Add(p => p.Id, "ssid")
            .Add(p => p.Value, string.Empty)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.NotNull(cut.Find("#ssid"));
    }

    [Fact]
    public void Label_IsPassedToTheOutlinedTextFieldElement()
    {
        var cut = Render<MdTextField>(parameters => parameters
            .Add(p => p.Label, "WiFi SSID")
            .Add(p => p.Value, string.Empty)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.Equal("WiFi SSID", cut.Find("md-outlined-text-field").GetAttribute("label"));
    }

    [Fact]
    public void Type_DefaultsToText()
    {
        var cut = Render<MdTextField>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.Equal("text", cut.Find("md-outlined-text-field").GetAttribute("type"));
    }

    [Fact]
    public void Type_CanBeOverriddenToPassword()
    {
        var cut = Render<MdTextField>(parameters => parameters
            .Add(p => p.Type, "password")
            .Add(p => p.Value, string.Empty)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.Equal("password", cut.Find("md-outlined-text-field").GetAttribute("type"));
    }
}
