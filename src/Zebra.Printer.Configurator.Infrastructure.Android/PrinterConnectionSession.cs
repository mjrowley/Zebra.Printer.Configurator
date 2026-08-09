using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Sdk.Comm;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Concrete backing for IPrinterConnectionSession - holds the real, already-open Connection. Internal
/// (not exposed via the public Core interface, which can't reference Zebra.Sdk.Comm.Connection);
/// consumers within this assembly (LinkOsPrinterConfigurationService, LinkOsPdfDirectService) cast
/// the IPrinterConnectionSession they receive down to this type to reach RunAsync. Safe because
/// PrinterConnectionSessionFactory is the only production implementation, the same as every other
/// single-implementation Core abstraction in this app.
/// </summary>
internal sealed class PrinterConnectionSession(Connection connection, IDisposable cancellationUnregister) : IPrinterConnectionSession
{
    public Task<T> RunAsync<T>(Func<Connection, T> func, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            return func(connection);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }, cancellationToken);

    public Task RunAsync(Action<Connection> action, CancellationToken cancellationToken) =>
        RunAsync<object?>(c =>
        {
            action(c);
            return null;
        }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        cancellationUnregister.Dispose();
        await Task.Run(connection.Close);
    }
}
