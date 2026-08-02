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
/// LinkOS-Android-Samples "TapScanConnectTCPBT" sample. <see cref="StartListening"/>/
/// <see cref="StopListening"/> (called from the Pairing page's lifecycle) control whether tag reads
/// are acted on; <see cref="INfcForegroundDispatch"/> (called from MainActivity) tracks whether the
/// Activity is actually resumed, since Android only allows foreground dispatch to be enabled then.
/// </summary>
public sealed class NfcPrinterDiscoveryService : IPrinterDiscoveryService, INfcForegroundDispatch
{
    private Activity? _activity;
    private NfcAdapter? _adapter;
    private bool _isListening;
    private bool _dispatchEnabled;

    public event EventHandler<PrinterDevice>? PrinterDiscovered;

    public void StartListening()
    {
        _isListening = true;
        TryEnableDispatch();
    }

    public void StopListening()
    {
        _isListening = false;
        TryDisableDispatch();
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

        // The tag carries multiple NDEF records (the app-specific data plus a URI fallback for
        // phones without this app), and their order isn't guaranteed, so every record's payload is
        // tried rather than assuming the app data is record[0].
        foreach (var payload in ExtractNdefPayloads(intent))
        {
            var device = NfcPrinterTagParser.TryParse(payload);
            if (device is not null)
            {
                PrinterDiscovered?.Invoke(this, device);
                return;
            }
        }
    }

    private void TryEnableDispatch()
    {
        if (_dispatchEnabled || !_isListening || _activity is null || _adapter is null)
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
