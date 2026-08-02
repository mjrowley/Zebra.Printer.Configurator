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
        Ssid = "Warehouse-WiFi",
        Password = "correcthorsebatterystaple",
        StaticIpAddress = "192.168.1.50",
        Netmask = "255.255.255.0",
        Gateway = "192.168.1.1",
    };

    private readonly IPrinterConfigurationService _configurationService = Substitute.For<IPrinterConfigurationService>();
    private readonly IPrinterRestartService _restartService = Substitute.For<IPrinterRestartService>();
    private readonly IPrinterConnectivityTestService _connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();

    [Fact]
    public void OnSuccessfulRun_NavigatesToResult()
    {
        _connectivityTestService.TestConnectionAsync(Configuration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Succeeded("CONNECTED"));
        var workflow = new PairAndConfigureWorkflow(_configurationService, _restartService, _connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession { Device = Device, Configuration = Configuration });

        var cut = Render<Progress>();

        cut.WaitForAssertion(() =>
        {
            var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            Assert.EndsWith("/result", navigation.Uri);
        });
        Assert.Equal(PairingWorkflowState.Succeeded, workflow.State);
    }

    [Fact]
    public void OnFailedRun_StillNavigatesToResult_LeavingFailureDetailsOnTheWorkflow()
    {
        _connectivityTestService.TestConnectionAsync(Configuration, Arg.Any<CancellationToken>())
            .Returns(ConnectionTestResult.Failed("Printer did not respond."));
        var workflow = new PairAndConfigureWorkflow(_configurationService, _restartService, _connectivityTestService);
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
    }

    [Fact]
    public void WhenSessionIncomplete_RedirectsToPairingWithoutRunningWorkflow()
    {
        var workflow = new PairAndConfigureWorkflow(_configurationService, _restartService, _connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession());

        Render<Progress>();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
        Assert.Equal(PairingWorkflowState.NotStarted, workflow.State);
    }
}
