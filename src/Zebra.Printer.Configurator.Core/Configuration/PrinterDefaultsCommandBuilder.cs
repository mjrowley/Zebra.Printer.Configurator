namespace Zebra.Printer.Configurator.Core.Configuration;

/// <summary>
/// Fixed ZD421 label-printing defaults applied to every printer alongside its WLAN configuration.
/// Unlike WlanConfigurationCommandBuilder these values aren't derived from user input - there's no
/// configuration model behind them, just the printer settings the user wants every unit configured
/// with. Pure and Zebra-SDK-independent so the values are unit-testable without a real
/// BluetoothConnection; the Infrastructure.Android service just replays these through SGD.SET like
/// any other command.
/// </summary>
public static class PrinterDefaultsCommandBuilder
{
    public static IReadOnlyList<(string Key, string Value)> BuildSetCommands() =>
    [
        ("media.printmode", "tear off"),
        ("device.friendly_name", "ZD421"),
        ("ezpl.media_type", "gap/notch"),
        ("ezpl.print_method", "direct thermal"),
        ("ezpl.print_width", "812"),
        ("ezpl.label_length_max", "7"),
    ];
}
