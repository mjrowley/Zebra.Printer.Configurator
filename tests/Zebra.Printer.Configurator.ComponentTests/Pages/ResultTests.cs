using Microsoft.Extensions.DependencyInjection;
using Bunit;
using NSubstitute;
using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;
using Zebra.Printer.Configurator.Core.Workflow;
using Zebra.Printer.Configurator.UI.Pages;

namespace Zebra.Printer.Configurator.ComponentTests.Pages;

public class ResultTests : BunitContext
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

    private async Task<(PairAndConfigureWorkflow Workflow, PairingSession Session)> RunWorkflowToCompletionAsync(ConnectionTestResult connectivityResult)
    {
        var configurationService = Substitute.For<IPrinterConfigurationService>();
        var restartService = Substitute.For<IPrinterRestartService>();
        var connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
        connectivityTestService.TestConnectionAsync(Device, Configuration, Arg.Any<CancellationToken>()).Returns(connectivityResult);

        var workflow = new PairAndConfigureWorkflow(configurationService, restartService, connectivityTestService);
        await workflow.RunAsync(Device, Configuration);

        var session = new PairingSession { Device = Device, Configuration = Configuration };
        Services.AddSingleton(workflow);
        Services.AddSingleton(session);

        return (workflow, session);
    }

    [Fact]
    public async Task SucceededWorkflow_ShowsConfirmedSsidAndIp()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Succeeded("CONNECTED"));

        var cut = Render<Result>();

        var successElement = cut.Find("[data-testid='result-success']");
        Assert.Contains("Warehouse-WiFi", successElement.TextContent);
        Assert.Contains("192.168.1.50", successElement.TextContent);
    }

    [Fact]
    public async Task FailedWorkflow_ShowsFailureReasonAndRetryButton()
    {
        await RunWorkflowToCompletionAsync(ConnectionTestResult.Failed("Printer did not respond."));

        var cut = Render<Result>();

        var failureElement = cut.Find("[data-testid='result-failure']");
        Assert.Contains("Printer did not respond.", failureElement.TextContent);
        Assert.NotNull(cut.Find("button"));
    }

    [Fact]
    public async Task ClickingRetry_ResetsSessionAndNavigatesToPairing()
    {
        var (_, session) = await RunWorkflowToCompletionAsync(ConnectionTestResult.Failed("Printer did not respond."));
        var cut = Render<Result>();

        cut.Find("button").Click();

        Assert.Null(session.Device);
        Assert.Null(session.Configuration);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
    }

    [Fact]
    public void WhenWorkflowHasNotFinished_RedirectsToPairing()
    {
        var configurationService = Substitute.For<IPrinterConfigurationService>();
        var restartService = Substitute.For<IPrinterRestartService>();
        var connectivityTestService = Substitute.For<IPrinterConnectivityTestService>();
        var workflow = new PairAndConfigureWorkflow(configurationService, restartService, connectivityTestService);
        Services.AddSingleton(workflow);
        Services.AddSingleton(new PairingSession());

        Render<Result>();

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", navigation.Uri);
    }
}
