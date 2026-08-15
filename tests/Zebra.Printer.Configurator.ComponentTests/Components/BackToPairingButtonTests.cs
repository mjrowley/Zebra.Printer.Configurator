using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Connectivity;
using Zebra.Printer.Configurator.Core.Firmware;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Components;

namespace Zebra.Printer.Configurator.ComponentTests.Components;

public class BackToPairingButtonTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private static readonly WlanConfiguration Configuration = new()
    {
        PrinterName = "ZD421",
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    private readonly IPrinterConnectionSessionFactory _sessionFactory = Substitute.For<IPrinterConnectionSessionFactory>();
    private readonly IPrinterConfigurationService _configurationService = Substitute.For<IPrinterConfigurationService>();
    private readonly IPdfDirectService _pdfDirectService = Substitute.For<IPdfDirectService>();
    private readonly IPrinterRestartService _restartService = Substitute.For<IPrinterRestartService>();
    private readonly IPrinterConnectivityTestService _connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
    private readonly PairAndConfigureWorkflow _workflow;
    private readonly FirmwareUpdateStatusMonitor _updateStatusMonitor = new();
    private readonly PrinterActivityMonitor _activityMonitor = new();
    private readonly PairingSession _session = new();
    private readonly PrinterConnectivityMonitor _connectivityMonitor = new();
    private readonly IWifiConnectivityMonitor _wifiMonitor = Substitute.For<IWifiConnectivityMonitor>();
    private readonly PrinterConnectionModeProvider _connectionModeProvider = new();

    public BackToPairingButtonTests()
    {
        _sessionFactory.OpenAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IPrinterConnectionSession>()));
        _workflow = new PairAndConfigureWorkflow(_sessionFactory, _configurationService, _pdfDirectService, _restartService, _connectivityTestService);
        Services.AddSingleton(_workflow);
        Services.AddSingleton(_updateStatusMonitor);
        Services.AddSingleton(_activityMonitor);
        Services.AddSingleton(_session);
        Services.AddSingleton(_connectivityMonitor);
        Services.AddSingleton(_wifiMonitor);
        Services.AddSingleton<IPrinterConnectionModeProvider>(_connectionModeProvider);
    }

    [Fact]
    public void WhenNoDeviceInSession_ButtonIsHidden()
    {
        var cut = Render<BackToPairingButton>();

        Assert.Empty(cut.FindAll("[data-testid='back-to-pairing-button']"));
    }

    [Fact]
    public void WhenDeviceInSession_ButtonIsVisibleAndEnabled()
    {
        _session.Device = Device;

        var cut = Render<BackToPairingButton>();

        Assert.False(cut.Find("[data-testid='back-to-pairing-button']").HasAttribute("disabled"));
    }

    [Fact]
    public void WhileWorkflowIsRunning_ButtonIsDisabled()
    {
        _session.Device = Device;
        // ApplyAsync never completes, holding the workflow at ApplyingConfiguration.
        _configurationService.ApplyAsync(Device, Configuration, Arg.Any<IPrinterConnectionSession>(), Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource().Task);

        var cut = Render<BackToPairingButton>();
        _ = _workflow.RunAsync(Device, Configuration);

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='back-to-pairing-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void WhileFirmwareUpdateIsRunning_ButtonIsDisabled()
    {
        _session.Device = Device;
        var cut = Render<BackToPairingButton>();

        _updateStatusMonitor.SetRunning();

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='back-to-pairing-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void WhileActivityMonitorIsBusy_ButtonIsDisabled()
    {
        _session.Device = Device;
        var cut = Render<BackToPairingButton>();

        var registration = _activityMonitor.Begin("Factory Reset");

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid='back-to-pairing-button']").HasAttribute("disabled")));

        registration.Dispose();

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid='back-to-pairing-button']").HasAttribute("disabled")));
    }

    [Fact]
    public void ClickingButton_ShowsConfirmationDialog()
    {
        _session.Device = Device;
        var cut = Render<BackToPairingButton>();

        cut.Find("[data-testid='back-to-pairing-button']").Click();

        Assert.NotNull(cut.Find("[data-testid='back-to-pairing-confirm-dialog']"));
    }

    [Fact]
    public void ClickingNo_HidesDialogWithoutNavigating()
    {
        _session.Device = Device;
        var cut = Render<BackToPairingButton>();
        cut.Find("[data-testid='back-to-pairing-button']").Click();

        cut.Find("[data-testid='back-to-pairing-confirm-no']").Click();

        Assert.Empty(cut.FindAll("[data-testid='back-to-pairing-confirm-dialog']"));
        Assert.Same(Device, _session.Device);
    }

    [Fact]
    public void ClickingYes_ResetsSessionConnectivityAndConnectionMode_AndNavigatesToRoot()
    {
        _session.Device = Device;
        _connectionModeProvider.UseWifi("192.168.1.50");
        var cut = Render<BackToPairingButton>();
        cut.Find("[data-testid='back-to-pairing-button']").Click();

        cut.Find("[data-testid='back-to-pairing-confirm-yes']").Click();

        Assert.Null(_session.Device);
        Assert.Null(_session.Configuration);
        Assert.Equal(PrinterConnectionMode.Bluetooth, _connectionModeProvider.Mode);
        _wifiMonitor.Received().Stop();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/", navigation.Uri));
    }
}
