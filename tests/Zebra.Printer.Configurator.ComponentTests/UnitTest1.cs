using Bunit;

namespace Zebra.Printer.Configurator.ComponentTests;

public class ScaffoldingSmokeTests : BunitContext
{
    [Fact]
    public void BUnit_CanRenderMarkup()
    {
        var cut = Render(builder => builder.AddMarkupContent(0, "<p>scaffolding-ok</p>"));

        Assert.Equal("<p>scaffolding-ok</p>", cut.Markup);
    }
}
