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
    public static IReadOnlyList<(string Key, string Value)> BuildSetCommands(string printerName) =>
    [
        ("media.printmode", "tear off"),
        ("device.friendly_name", printerName),
        ("ezpl.media_type", "gap/notch"),
        ("ezpl.print_method", "direct thermal"),
        ("ezpl.print_width", "812"),
        ("ezpl.label_length_max", "7"),
    ];
}
