using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Logging;

namespace Zebra.Printer.Configurator.UnitTests.Logging;

public class AppLogTests
{
    [Fact]
    public void Log_AddsEntryWithGivenMessageAndLevel()
    {
        var log = new AppLog();

        log.Log("Printer discovered", LogLevel.Success);

        var entry = Assert.Single(log.Entries);
        Assert.Equal("Printer discovered", entry.Message);
        Assert.Equal(LogLevel.Success, entry.Level);
    }

    [Fact]
    public void Log_DefaultsToInfoLevel()
    {
        var log = new AppLog();

        log.Log("Waiting for NFC tap...");

        Assert.Equal(LogLevel.Info, log.Entries[0].Level);
    }

    [Fact]
    public void Log_PreservesOrderAcrossMultipleEntries()
    {
        var log = new AppLog();

        log.Log("First");
        log.Log("Second");
        log.Log("Third");

        Assert.Equal(["First", "Second", "Third"], log.Entries.Select(e => e.Message));
    }

    [Fact]
    public void Log_RaisesEntryLoggedWithTheNewEntry()
    {
        var log = new AppLog();
        LogEntry? raised = null;
        log.EntryLogged += (_, entry) => raised = entry;

        log.Log("Connecting to printer...", LogLevel.Info);

        Assert.NotNull(raised);
        Assert.Equal("Connecting to printer...", raised!.Message);
    }
}
