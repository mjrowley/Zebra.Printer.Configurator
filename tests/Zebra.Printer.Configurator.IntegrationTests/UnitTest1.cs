namespace Zebra.Printer.Configurator.IntegrationTests;

// CI-runnable subset lives here: tests against a fake TCP server that speaks the SGD wire
// protocol (see plan Phase 5/6). Real on-device NFC/Bluetooth tests are a separate,
// manually-triggered device-lab suite since they require physical hardware.
public class ScaffoldingSmokeTests
{
    [Test]
    public void IntegrationTestProject_IsWiredUpAndRunnable()
    {
        Assert.Pass();
    }
}
