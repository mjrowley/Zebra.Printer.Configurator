using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
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
}
