using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.ApplicationModel;
using Zebra.Printer.Configurator.Infrastructure.Android;

namespace Zebra.Printer.Configurator.App;

// LaunchMode.SingleTask - this is a single-window app with process-wide DI singletons
// (PairingSession, FirmwareUpdateStatusMonitor, ...) backing its Blazor UI, so more than one
// MainActivity instance existing at once is never valid. Without this, the default "standard"
// launch mode let a notification tap (FirmwareUpdateForegroundService's PendingIntent, built from
// a plain PackageManager.GetLaunchIntentForPackage() intent with no ClearTop/SingleTop flag) spin
// up a *second* instance stacked on top of the backgrounded one instead of resuming it - confirmed
// as the cause of a real bug where two independently-mounted PrinterVersionAlert components both
// reacted to the same FirmwareUpdateStatusMonitor.Changed event and ran concurrent Bluetooth/WiFi
// version-check connections against a printer that had just finished rebooting, corrupting one of
// the reads into a false "unsupported model" result.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	// NFC foreground dispatch can only be enabled while this Activity is resumed, so its
	// lifecycle is forwarded to the service rather than the service managing it independently.
	private INfcForegroundDispatch NfcDispatch =>
		IPlatformApplication.Current!.Services.GetRequiredService<INfcForegroundDispatch>();

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		// Android 15+ (targetSdk 35+) enforces edge-to-edge by default and ignores
		// Window.SetDecorFitsSystemWindows(true), so content draws behind the status bar/gesture
		// nav bar unless padded for explicitly. Insets are applied to the DecorView so this covers
		// the BlazorWebView regardless of how MAUI lays it out inside the page.
		ViewCompat.SetOnApplyWindowInsetsListener(Window!.DecorView, new SystemBarsInsetsListener());
	}

	private sealed class SystemBarsInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
	{
		public WindowInsetsCompat? OnApplyWindowInsets(global::Android.Views.View? view, WindowInsetsCompat? insets)
		{
			var systemBars = insets?.GetInsets(WindowInsetsCompat.Type.SystemBars());
			if (systemBars is { } bars)
			{
				view?.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
			}

			return insets;
		}
	}

	protected override void OnResume()
	{
		base.OnResume();
		NfcDispatch.OnActivityResumed(this);
	}

	protected override void OnPause()
	{
		NfcDispatch.OnActivityPaused();
		base.OnPause();
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		if (intent is not null)
		{
			NfcDispatch.OnNewIntent(intent);
		}
	}

	// Required for Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<T>() (used to request
	// BLUETOOTH_SCAN/BLUETOOTH_CONNECT) to resolve its awaited Task with the user's response.
	public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
	{
		Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
		base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
	}
}
