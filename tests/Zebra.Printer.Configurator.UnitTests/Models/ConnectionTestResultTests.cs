using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UnitTests.Models;

public class ConnectionTestResultTests
{
    [Fact]
    public void Succeeded_SetsSuccessAndWlanState()
    {
        var result = ConnectionTestResult.Succeeded("CONNECTED", "192.168.1.50");

        Assert.True(result.Success);
        Assert.Equal("CONNECTED", result.ConfirmedWlanState);
        Assert.Equal("192.168.1.50", result.ResolvedIpAddress);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Failed_SetsFailureReason()
    {
        var result = ConnectionTestResult.Failed("Timed out waiting for printer to reconnect.");

        Assert.False(result.Success);
        Assert.Equal("Timed out waiting for printer to reconnect.", result.FailureReason);
        Assert.Null(result.ConfirmedWlanState);
        Assert.Null(result.ResolvedIpAddress);
    }

    [Fact]
    public void Failed_CanCarryAResolvedIpAddress()
    {
        // Used for a Static configuration whose "intended" IP is known even though the printer
        // never actually confirmed reachability there.
        var result = ConnectionTestResult.Failed("Printer did not respond.", "192.168.1.50");

        Assert.False(result.Success);
        Assert.Equal("192.168.1.50", result.ResolvedIpAddress);
    }
}
