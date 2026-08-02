using Microsoft.Extensions.Logging;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.Infrastructure.Android;

namespace Zebra.Printer.Configurator.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// Single instance backs both interfaces: IPrinterDiscoveryService is what the UI/workflow
		// depends on, INfcForegroundDispatch is what MainActivity forwards Activity lifecycle/intents to.
		builder.Services.AddSingleton<NfcPrinterDiscoveryService>();
		builder.Services.AddSingleton<IPrinterDiscoveryService>(sp => sp.GetRequiredService<NfcPrinterDiscoveryService>());
		builder.Services.AddSingleton<INfcForegroundDispatch>(sp => sp.GetRequiredService<NfcPrinterDiscoveryService>());
		builder.Services.AddSingleton<IHostNetworkInfoService, HostNetworkInfoService>();

		// Single instance backs both interfaces: configuring the printer and restarting it both
		// happen over the same kind of Bluetooth connection, back-to-back in the pairing workflow.
		builder.Services.AddSingleton<LinkOsPrinterConfigurationService>();
		builder.Services.AddSingleton<IPrinterConfigurationService>(sp => sp.GetRequiredService<LinkOsPrinterConfigurationService>());
		builder.Services.AddSingleton<IPrinterRestartService>(sp => sp.GetRequiredService<LinkOsPrinterConfigurationService>());
		builder.Services.AddSingleton<IPrinterConnectivityTestService, LinkOsConnectivityTestService>();

		// Both singletons: single-window app, one pairing attempt in flight at a time.
		builder.Services.AddSingleton<PairingSession>();
		builder.Services.AddSingleton<PairAndConfigureWorkflow>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
