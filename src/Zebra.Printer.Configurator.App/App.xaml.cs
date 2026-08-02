namespace Zebra.Printer.Configurator.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = "Zebra.Printer.Configurator.App" };
	}
}
