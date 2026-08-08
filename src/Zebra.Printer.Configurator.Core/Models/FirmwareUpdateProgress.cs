namespace Zebra.Printer.Configurator.Core.Models;

public enum FirmwareUpdateStage
{
    /// <summary>Transferring the firmware file to the printer.</summary>
    Downloading,

    /// <summary>Transfer complete - the printer is flashing the new firmware and rebooting.</summary>
    AwaitingReboot,

    /// <summary>The printer has come back online running the new firmware.</summary>
    Complete,
}

public sealed record FirmwareUpdateProgress
{
    public required FirmwareUpdateStage Stage { get; init; }

    /// <summary>Only meaningful during Downloading - null at other stages.</summary>
    public int? BytesWritten { get; init; }

    public int? TotalBytes { get; init; }
}
