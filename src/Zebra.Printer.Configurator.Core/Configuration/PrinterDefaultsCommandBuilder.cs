using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Configuration;

/// <summary>
/// Fixed ZD421 label-printing defaults applied to every printer alongside its WLAN configuration.
/// Unlike WlanConfigurationCommandBuilder most of these values aren't derived from user input -
/// just the printer settings the user wants every unit configured with - except
/// device.friendly_name, which takes the user-entered Printer Name (WlanConfiguration.PrinterName)
/// rather than being fixed like its neighbors here. Pure and Zebra-SDK-independent so the values
/// are unit-testable without a real BluetoothConnection; the Infrastructure.Android service just
/// replays these through SGD.SET like any other command.
/// </summary>
public static class PrinterDefaultsCommandBuilder
{
    // 100mm die-cut label (measured on the actual media in use) / 25.4mm-per-inch * 203dpi = 799.2
    // dots, floored rather than rounded up so the configured print width never claims more canvas
    // than the label physically has - the previous value (812, i.e. 101.6mm) was sized to the
    // backing paper's ~104mm width instead of the narrower die-cut label, which left the printer
    // treating dots the physical label doesn't cover as printable, right-shifting the printable
    // area and clipping the left edge against the media guide.
    private const string PrintWidthDots = "799";

    // Confirmed on-device against the 100mm label: -16 dots noticeably improves both print paths
    // (bag tags via PrintStoredFormat, and PDF Direct labels once apl.settings is also set below) -
    // without it, content sits hard against the media's left edge and clips. Unlike ^LH inside the
    // bag tag templates (which only ever affected that stored format and, via the ^JUS that used to
    // sit next to it, kept re-saving itself over this value - see the templates' own history), this
    // is the device-level setting and applies to every print path uniformly. Key is zpl.left_position
    // (not ezpl.left_position - a previous version of this file had that wrong).
    private const string LeftPositionDots = "-16";

    // PDF Direct (apl.enable "pdf") does not read the loaded media's actual size and scale the PDF
    // to fit it by default - confirmed on-device that without this, the PDF prints at its own native
    // page size regardless of the label loaded, which is what caused labels to sit hard against the
    // left edge with excess blank space on the right (a mismatch between the PDF's page size and the
    // physical label, not a printer calibration problem - ezpl.print_width/left_position alone did
    // nothing for this print path). "scale-to-fit" tells the PDF virtual device to scale the
    // rendered page to the printer's current print width/length instead of printing it 1:1.
    private const string AplSettings = "scale-to-fit";

    // PDF Direct's enabled value - shared with LinkOsPdfDirectService (which sets/checks it through
    // its own careful, check-before-push flow rather than this class's plain SGD.SET list) so both
    // that flow and BuildExpectedDiagnosticValues below reference the one definition instead of
    // duplicating the literal.
    public const string PdfEnabledValue = "pdf";

    // Everything BuildSetCommands sends except device.friendly_name, which isn't fixed - it takes
    // the user-entered printer name, so it has no known target until a WlanConfiguration exists.
    private static readonly IReadOnlyList<(string Key, string Value)> FixedDefaults =
    [
        ("media.printmode", "tear off"),
        ("ezpl.media_type", "gap/notch"),
        ("ezpl.print_method", "direct thermal"),
        ("ezpl.print_width", PrintWidthDots),
        ("ezpl.label_length_max", "7"),
        ("zpl.left_position", LeftPositionDots),
        ("apl.settings", AplSettings),
    ];

    public static IReadOnlyList<(string Key, string Value)> BuildSetCommands(string printerName) =>
        [("device.friendly_name", printerName), .. FixedDefaults];

    /// <summary>
    /// The subset of BuildExpectedDiagnosticValues that's known without a WlanConfiguration at all -
    /// the fixed label-printing defaults and apl.enable, i.e. everything that doesn't depend on a
    /// particular pairing attempt's user-entered name/SSID/IP. Lets the Check Configuration screen
    /// colour-code these for an already-paired printer being inspected on the Pairing page itself
    /// (e.g. one configured in an earlier session) - PairingSession.Configuration doesn't exist yet
    /// at that point (it's only set once the user submits the Configure form), but these targets
    /// don't need it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildFixedDiagnosticDefaults()
    {
        var expected = FixedDefaults.ToDictionary(c => c.Key, c => c.Value);
        expected["apl.enable"] = PdfEnabledValue;
        return expected;
    }

    /// <summary>
    /// Every SGD key/value pair this app expects a fully-configured printer to report, for the Check
    /// Configuration screen's colour-coding - the union of the fixed label-printing defaults above,
    /// the WLAN settings for this pairing attempt, and apl.enable (set via LinkOsPdfDirectService's
    /// own flow, not BuildSetCommands, but still something this app expects to be "pdf").
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildExpectedDiagnosticValues(string printerName, WlanConfiguration configuration)
    {
        var expected = new Dictionary<string, string>(BuildFixedDiagnosticDefaults())
        {
            ["device.friendly_name"] = printerName,
        };

        foreach (var (key, value) in WlanConfigurationCommandBuilder.BuildSetCommands(configuration))
        {
            expected[key] = value;
        }

        return expected;
    }
}
