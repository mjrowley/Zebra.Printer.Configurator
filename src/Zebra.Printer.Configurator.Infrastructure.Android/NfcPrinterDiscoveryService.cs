using System.Text;
using Android.App;
using Android.Content;
using Android.Nfc;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Parsing;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// NFC discovery via NfcAdapter foreground dispatch, modeled on Zebra's own
/// LinkOS-Android-Samples "TapScanConnectTCPBT" sample. Foreground dispatch itself is armed purely
/// by <see cref="INfcForegroundDispatch"/>'s Activity-resumed/paused lifecycle (called from
/// MainActivity), independent of <see cref="StartListening"/>/<see cref="StopListening"/> (called
/// from the Pairing page's lifecycle) - MAUI's BlazorWebView takes noticeably longer to reach the
/// Pairing page's OnInitialized than Activity.OnResume fires, so gating dispatch registration on
/// StartListening left a real window on cold start where a tap fell outside dispatch entirely and
/// hit Android's normal tag-handling chooser instead of this app. StartListening/StopListening
/// still gate whether a discovered tag is actually acted on, via the check in OnNewIntent.
/// </summary>
public sealed class NfcPrinterDiscoveryService(IAppLog appLog) : IPrinterDiscoveryService, INfcForegroundDispatch
{
    private Activity? _activity;
    private NfcAdapter? _adapter;
    private bool _isListening;
    private bool _dispatchEnabled;

    public event EventHandler<PrinterDevice>? PrinterDiscovered;

    public void StartListening()
    {
        _isListening = true;
        appLog.Log("Waiting for NFC tap...");
    }

    public void StopListening()
    {
        _isListening = false;
    }

    public void OnActivityResumed(Activity activity)
    {
        _activity = activity;
        _adapter = NfcAdapter.GetDefaultAdapter(activity);
        TryEnableDispatch();
    }

    public void OnActivityPaused()
    {
        TryDisableDispatch();
    }

    public void OnNewIntent(Intent intent)
    {
        if (!_isListening)
        {
            return;
        }

        appLog.Log("NFC tag detected. Reading printer data...");

        // The tag carries multiple NDEF records (the app-specific data plus a URI fallback for
        // phones without this app), and their order isn't guaranteed, so every record's payload is
        // tried rather than assuming the app data is record[0].
        foreach (var payload in ExtractNdefPayloads(intent))
        {
            var device = NfcPrinterTagParser.TryParse(payload);
            if (device is not null)
            {
                appLog.Log($"Printer identified (Bluetooth MAC: {device.BluetoothMacAddress}).", LogLevel.Success);
                PrinterDiscovered?.Invoke(this, device);
                return;
            }
        }

        appLog.Log("NFC tag did not contain recognizable Zebra printer data.", LogLevel.Warning);
    }

    private void TryEnableDispatch()
    {
        if (_dispatchEnabled || _activity is null || _adapter is null)
        {
            return;
        }

        var intent = new Intent(_activity, _activity.GetType()).AddFlags(ActivityFlags.SingleTop);
        var pendingIntent = PendingIntent.GetActivity(_activity, 0, intent, PendingIntentFlags.Mutable);

        var filters = new[]
        {
            new IntentFilter(NfcAdapter.ActionTagDiscovered),
            new IntentFilter(NfcAdapter.ActionNdefDiscovered),
            CreateViewIntentFilter(),
        };

        _adapter.EnableForegroundDispatch(_activity, pendingIntent, filters, null);
        _dispatchEnabled = true;
    }

    private static IntentFilter CreateViewIntentFilter()
    {
        // The printer's NFC tag also carries a URI record (Zebra's support page) as a fallback for
        // phones without this app installed. When an NDEF message's leading record is a well-known
        // URI type, Android's NFC dispatcher routes the tag as ACTION_VIEW instead of
        // ACTION_NDEF_DISCOVERED - without this filter that intent falls outside foreground
        // dispatch entirely and the OS shows its own app-chooser dialog for it instead of handing
        // the tag to this app.
        var filter = new IntentFilter(Intent.ActionView);
        filter.AddDataScheme("http");
        filter.AddDataScheme("https");
        return filter;
    }

    private void TryDisableDispatch()
    {
        if (!_dispatchEnabled || _activity is null || _adapter is null)
        {
            return;
        }

        _adapter.DisableForegroundDispatch(_activity);
        _dispatchEnabled = false;
    }

    private static IEnumerable<string> ExtractNdefPayloads(Intent intent)
    {
        // minSdk is 33, so the typed overload (added in API 33) is always available -
        // no need for the deprecated untyped GetParcelableArrayExtra(string).
        var rawMessages = intent.GetParcelableArrayExtra(NfcAdapter.ExtraNdefMessages, Java.Lang.Class.FromType(typeof(NdefMessage)));
        if (rawMessages is not { Length: > 0 })
        {
            yield break;
        }

        foreach (var rawMessage in rawMessages)
        {
            if (rawMessage is not NdefMessage message)
            {
                continue;
            }

            foreach (var record in message.GetRecords() ?? [])
            {
                var payloadBytes = record.GetPayload();
                if (payloadBytes is not null)
                {
                    yield return Encoding.UTF8.GetString(payloadBytes);
                }
            }
        }
    }
}
