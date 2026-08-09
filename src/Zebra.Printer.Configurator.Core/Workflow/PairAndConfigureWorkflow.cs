using Zebra.Printer.Configurator.Core.Abstractions;
using Zebra.Printer.Configurator.Core.Models;

namespace Zebra.Printer.Configurator.Core.Workflow;

/// <summary>
/// Orchestrates the configure -> restart -> verify sequence once a printer has been discovered and
/// its WLAN configuration entered. Depends only on the service abstractions, so it's fully
/// unit-testable with faked services - no Android/SDK involvement.
/// </summary>
public sealed class PairAndConfigureWorkflow(
    IPrinterConnectionSessionFactory sessionFactory,
    IPrinterConfigurationService configurationService,
    IPdfDirectService pdfDirectService,
    IPrinterRestartService restartService,
    IPrinterConnectivityTestService connectivityTestService)
{
    public PairingWorkflowState State { get; private set; } = PairingWorkflowState.NotStarted;

    public ConnectionTestResult? Result { get; private set; }

    public string? FailureReason { get; private set; }

    public event EventHandler? StateChanged;

    public async Task RunAsync(PrinterDevice device, WlanConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Result = null;
        FailureReason = null;

        try
        {
            // Shared across all three pre-restart steps rather than each opening/closing its own
            // connection - see PrinterConnectionSession's doc comment.
            var session = await sessionFactory.OpenAsync(device, cancellationToken).ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                SetState(PairingWorkflowState.ApplyingConfiguration);
                await configurationService.ApplyAsync(device, configuration, session, cancellationToken).ConfigureAwait(false);

                SetState(PairingWorkflowState.EnablingPdfDirect);
                await pdfDirectService.EnsureEnabledAsync(device, session, cancellationToken).ConfigureAwait(false);

                SetState(PairingWorkflowState.Restarting);
                await restartService.RestartAsync(device, session, cancellationToken).ConfigureAwait(false);
            }

            SetState(PairingWorkflowState.TestingConnection);
            var result = await connectivityTestService.TestConnectionAsync(device, configuration, cancellationToken).ConfigureAwait(false);

            Result = result;
            FailureReason = result.Success ? null : result.FailureReason;
            SetState(result.Success ? PairingWorkflowState.Succeeded : PairingWorkflowState.Failed);
        }
        catch (OperationCanceledException)
        {
            // Reset rather than leave State stuck mid-step - Workflow is a DI singleton the header's
            // Cancel button keeps reading on every page thereafter, so an unreset state would leave
            // it looking permanently in-progress.
            SetState(PairingWorkflowState.NotStarted);
            throw;
        }
        catch (Exception ex)
        {
            FailureReason = ex.Message;
            SetState(PairingWorkflowState.Failed);
        }
    }

    private void SetState(PairingWorkflowState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
