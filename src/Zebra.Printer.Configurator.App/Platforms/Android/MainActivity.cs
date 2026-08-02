using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Microsoft.Maui.ApplicationModel;
using Zebra.Printer.Configurator.Infrastructure.Android;

namespace Zebra.Printer.Configurator.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	// NFC foreground dispatch can only be enabled while this Activity is resumed, so its
	// lifecycle is forwarded to the service rather than the service managing it independently.
	private INfcForegroundDispatch NfcDispatch =>
		IPlatformApplication.Current!.Services.GetRequiredService<INfcForegroundDispatch>();

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
