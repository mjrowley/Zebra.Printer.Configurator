using Zebra.Printer.Configurator.Core.Connectivity;

namespace Zebra.Printer.Configurator.UnitTests.Connectivity;

public class RetryPollerTests
{
    [Fact]
    public async Task PollUntilAsync_ReturnsTrue_WhenFirstAttemptSucceeds()
    {
        var attempts = 0;

        var result = await RetryPoller.PollUntilAsync(
            attempt: () => { attempts++; return Task.FromResult(true); },
            timeout: TimeSpan.FromMilliseconds(200),
            interval: TimeSpan.FromMilliseconds(20));

        Assert.True(result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task PollUntilAsync_RetriesUntilSuccess()
    {
        var attempts = 0;

        var result = await RetryPoller.PollUntilAsync(
            attempt: () => { attempts++; return Task.FromResult(attempts >= 3); },
            timeout: TimeSpan.FromSeconds(2),
            interval: TimeSpan.FromMilliseconds(10));

        Assert.True(result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task PollUntilAsync_ReturnsFalse_WhenTimeoutElapsesWithoutSuccess()
    {
        var result = await RetryPoller.PollUntilAsync(
            attempt: () => Task.FromResult(false),
            timeout: TimeSpan.FromMilliseconds(100),
            interval: TimeSpan.FromMilliseconds(20));

        Assert.False(result);
    }

    [Fact]
    public async Task PollUntilAsync_ReturnsFalse_WhenCancelledExternally()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await RetryPoller.PollUntilAsync(
            attempt: () => Task.FromResult(false),
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(20),
            cancellationToken: cts.Token);

        Assert.False(result);
    }
}
