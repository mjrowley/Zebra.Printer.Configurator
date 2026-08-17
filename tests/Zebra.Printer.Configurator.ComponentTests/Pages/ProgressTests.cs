using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Pages;

namespace Zebra.Printer.Configurator.ComponentTests.Pages;

public class ProgressTests : BunitContext
{
    private static readonly PrinterDevice Device = new() { BluetoothMacAddress = "AABBCCDDEEFF" };

    private static readonly WlanConfiguration Configuration = new()
    {
        PrinterName = "ZD421",
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        IpAddressMode = WlanIpAddressMode.Static,
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    private readonly IPrinterConnectionSessionFactory _sessionFactory = Substitute.For<IPrinterConnectionSessionFactory>();
    private readonly IPrinterConfigurationService _configurationService = Substitute.For<IPrinterConfigurationService>();
    private readonly IPdfDirectService _pdfDirectService = Substitute.For<IPdfDirectService>();
    private readonly IPrinterRestartService _restartService = Substitute.For<IPrinterRestartService>();
    private readonly IPrinterConnectivityTestService _connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
    private readonly IWifiConnectivityMonitor _wifiMonitor = Substitute.For<IWifiConnectivityMonitor>();
    private readonly PrinterOperationCancellation _cancellation = new();

    public ProgressTests()
    {
        _sessionFactory.OpenAsync(Arg.Any<PrinterDevice>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IPrinterConnectionSession>()));
        Services.AddSingleton(_wifiMonitor);
        Services.AddSingleton(_cancellation);
    }

    [Fact]
    public void OnSuccessfulRun_NavigatesToResult()
    {
        _connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Succeeded("CONNECTED", Configuration.StaticIpAddress));
        var workflow = new PairAndConfigureWorkflow(_sessionFactory, _configurationService, _pdfDirectService, _restartService, _connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession { Device = Device, Configuration = Configuration });

        var cut = Render<Progress>();

        cut.WaitForAssertion(() =>
        {
            var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            Assert.EndsWith("/result", navigation.Uri);
        });
        Assert.Equal(PairingWorkflowState.Succeeded, workflow.State);
        _wifiMonitor.Received().Start(Configuration.StaticIpAddress);
    }

    [Fact]
    public void OnFailedRun_StillNavigatesToResult_LeavingFailureDetailsOnTheWorkflow()
    {
        _connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Failed("Printer did not respond."));
        var workflow = new PairAndConfigureWorkflow(_sessionFactory, _configurationService, _pdfDirectService, _restartService, _connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession { Device = Device, Configuration = Configuration });

        var cut = Render<Progress>();

        cut.WaitForAssertion(() =>
        {
            var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            Assert.EndsWith("/result", navigation.Uri);
        });
        Assert.Equal(PairingWorkflowState.Failed, workflow.State);
        Assert.Equal("Printer did not respond.", workflow.FailureReason);
        _wifiMonitor.DidNotReceive().Start(Arg.Any<string>());
    }

    [Fact]
    public void OnDhcpRunWithNoResolvedIpAddress_DoesNotStartWifiMonitor()
    {
        // A Dhcp configuration that never discovers an address (e.g. connectivity test timed out
        // before reading one back) has nothing to monitor - unlike Static, there's no "intended" IP
        // to fall back to.
        var dhcpConfiguration = Configuration with { IpAddressMode = WlanIpAddressMode.Dhcp, StaticIpAddress = string.Empty };
        _connectivityTestService.TestConnectionAsync(Device, dhcpConfiguration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Failed("Printer did not respond."));
        var workflow = new PairAndConfigureWorkflow(_sessionFactory, _configurationService, _pdfDirectService, _restartService, _connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession { Device = Device, Configuration = dhcpConfiguration });

        var cut = Render<Progress>();

        cut.WaitForAssertion(() =>
        {
            var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            Assert.EndsWith("/result", navigation.Uri);
        });
        _wifiMonitor.DidNotReceive().Start(Arg.Any<string>());
    }

    [Fact]
    public void WhenCancelled_DoesNotNavigateToResult()
    {
        // Mirrors what the header's Cancel button does: force-close the connection, which surfaces
        // here as ApplyAsync throwing OperationCanceledException.
        _configurationService.ApplyAsync(Device, Configuration, Arg.Any<IPrinterConnectionSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new OperationCanceledException()));
        var workflow = new PairAndConfigureWorkflow(_sessionFactory, _configurationService, _pdfDirectService, _restartService, _connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession { Device = Device, Configuration = Configuration });

        var cut = Render<Progress>();

        cut.WaitForAssertion(() => Assert.Equal(PairingWorkflowState.NotStarted, workflow.State));
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.DoesNotContain("/result", navigation.Uri);
        _wifiMonitor.DidNotReceive().Start(Arg.Any<string>());
    }

    [Fact]
    public void WhenSessionIncomplete_RedirectsToPairingWithoutRunningWorkflow()
    {
        var workflow = new PairAndConfigureWorkflow(_sessionFactory, _configurationService, _pdfDirectService, _restartService, _connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession());

        Render<Progress>();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
        Assert.Equal(PairingWorkflowState.NotStarted, workflow.State);
    }
}
