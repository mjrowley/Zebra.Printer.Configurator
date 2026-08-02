using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.UnitTests.Models;

public class ConnectionTestResultTests
{
    [Fact]
    public void Succeeded_SetsSuccessAndWlanState()
    {
        var result = ConnectionTestResult.Succeeded("CONNECTED");

        Assert.True(result.Success);
        Assert.Equal("CONNECTED", result.ConfirmedWlanState);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Failed_SetsFailureReason()
    {
        var result = ConnectionTestResult.Failed("Timed out waiting for printer to reconnect.");

        Assert.False(result.Success);
        Assert.Equal("Timed out waiting for printer to reconnect.", result.FailureReason);
        Assert.Null(result.ConfirmedWlanState);
    }
}
