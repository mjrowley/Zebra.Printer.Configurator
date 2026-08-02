namespace Zebra.Printer.Configurator.Core.Connectivity;

/// <summary>
/// Fixed-interval polling until an attempt succeeds or the timeout elapses. Used to wait out a
/// printer reboot (~30-45s) without needing true exponential backoff - the reboot timeline is
/// predictable, so a simple repeated probe is enough.
/// </summary>
public static class RetryPoller
{
    public static async Task<bool> PollUntilAsync(
        Func<Task<bool>> attempt,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        while (true)
        {
            if (await attempt().ConfigureAwait(false))
            {
                return true;
            }

            if (cts.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                await Task.Delay(interval, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
