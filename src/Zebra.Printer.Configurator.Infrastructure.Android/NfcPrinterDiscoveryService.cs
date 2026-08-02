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

        var payload = ExtractNdefPayload(intent);
        var device = NfcPrinterTagParser.TryParse(payload);
        if (device is not null)
        {
            PrinterDiscovered?.Invoke(this, device);
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
        };

        _adapter.EnableForegroundDispatch(_activity, pendingIntent, filters, null);
        _dispatchEnabled = true;
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

    private static string? ExtractNdefPayload(Intent intent)
    {
        // minSdk is 33, so the typed overload (added in API 33) is always available -
        // no need for the deprecated untyped GetParcelableArrayExtra(string).
        var rawMessages = intent.GetParcelableArrayExtra(NfcAdapter.ExtraNdefMessages, Java.Lang.Class.FromType(typeof(NdefMessage)));
        if (rawMessages is not { Length: > 0 } || rawMessages[0] is not NdefMessage message)
        {
            return null;
        }

        var records = message.GetRecords();
        if (records is not { Length: > 0 })
        {
            return null;
        }

        var payloadBytes = records[0].GetPayload();
        return payloadBytes is null ? null : Encoding.UTF8.GetString(payloadBytes);
    }
}
