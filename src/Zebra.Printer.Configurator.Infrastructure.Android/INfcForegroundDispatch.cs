using Android.App;
using Android.Content;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// NFC foreground dispatch can only be enabled while the Android Activity is resumed, so MainActivity
/// forwards its OnResume/OnPause/OnNewIntent calls here rather than this being driven purely through
/// the platform-agnostic IPrinterDiscoveryService interface.
/// </summary>
public interface INfcForegroundDispatch
{
    void OnActivityResumed(Activity activity);

    void OnActivityPaused();

    void OnNewIntent(Intent intent);
}
