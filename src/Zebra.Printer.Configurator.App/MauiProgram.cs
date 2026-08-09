using Microsoft.Extensions.Logging;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Logging;
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

		builder.Services.AddSingleton<IAppLog, AppLog>();
		builder.Services.AddSingleton<IAppVersionProvider, AppVersionProvider>();

		// All three singletons: single-window app, one target printer's connectivity display and
		// active transport tracked at a time, backing the header's Bluetooth/WiFi indicators and
		// "Connect via WiFi".
		builder.Services.AddSingleton<PrinterConnectivityMonitor>();
		builder.Services.AddSingleton<IWifiConnectivityMonitor, WifiConnectivityMonitor>();
		builder.Services.AddSingleton<IPrinterConnectionModeProvider, PrinterConnectionModeProvider>();

		// Single instance backs both interfaces: IPrinterDiscoveryService is what the UI/workflow
		// depends on, INfcForegroundDispatch is what MainActivity forwards Activity lifecycle/intents to.
		builder.Services.AddSingleton<NfcPrinterDiscoveryService>();
		builder.Services.AddSingleton<IPrinterDiscoveryService>(sp => sp.GetRequiredService<NfcPrinterDiscoveryService>());
		builder.Services.AddSingleton<INfcForegroundDispatch>(sp => sp.GetRequiredService<NfcPrinterDiscoveryService>());
		builder.Services.AddSingleton<IHostNetworkInfoService, HostNetworkInfoService>();
		builder.Services.AddSingleton<IBluetoothPermissionService, BluetoothPermissionService>();
		builder.Services.AddSingleton<IBluetoothPairingService, BluetoothPairingService>();

		// Temporary diagnostic, not part of the pairing flow itself - see its own doc comment.
		// Remove once the "Can't connect" OS dialog investigation is concluded.
		builder.Services.AddSingleton<IBluetoothProfileDiagnostics, BluetoothProfileDiagnostics>();

		// Single instance backs all four interfaces: configuring the printer, restarting it,
		// factory-resetting it, and reading its configuration back all happen over the same kind of
		// Bluetooth connection.
		builder.Services.AddSingleton<LinkOsPrinterConfigurationService>();
		builder.Services.AddSingleton<IPrinterConfigurationService>(sp => sp.GetRequiredService<LinkOsPrinterConfigurationService>());
		builder.Services.AddSingleton<IPrinterRestartService>(sp => sp.GetRequiredService<LinkOsPrinterConfigurationService>());
		builder.Services.AddSingleton<IPrinterFactoryResetService>(sp => sp.GetRequiredService<LinkOsPrinterConfigurationService>());
		builder.Services.AddSingleton<IPrinterConfigurationReader>(sp => sp.GetRequiredService<LinkOsPrinterConfigurationService>());
		builder.Services.AddSingleton<IPrinterConnectivityTestService, LinkOsConnectivityTestService>();
		builder.Services.AddSingleton<IPdfDirectService, LinkOsPdfDirectService>();
		builder.Services.AddSingleton<IPrinterConnectionSessionFactory, PrinterConnectionSessionFactory>();
		builder.Services.AddSingleton<IPrinterVersionCheckService, LinkOsPrinterVersionCheckService>();
		builder.Services.AddSingleton<IPrinterFirmwareUpdateService, LinkOsFirmwareUpdateService>();
		builder.Services.AddSingleton<FirmwareUpdateStatusMonitor>();
		builder.Services.AddSingleton<IFirmwareUpdateLauncher, FirmwareUpdateLauncher>();

		// All three singletons: single-window app, one pairing attempt in flight at a time.
		builder.Services.AddSingleton<PairingSession>();
		builder.Services.AddSingleton<PairAndConfigureWorkflow>();
		builder.Services.AddSingleton<PrinterOperationCancellation>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// FirmwareUpdateForegroundService and BluetoothPairingReceiver are both instantiated
		// directly by Android (a started Service, a manifest-declared BroadcastReceiver), not
		// through this container, so they need a way to reach these same registered services -
		// see AppServiceLocator's own doc comment for why.
		AppServiceLocator.Services = app.Services;

		return app;
	}
}
