namespace Zebra.Printer.Configurator.Core.Workflow;

/// <summary>
/// States mirror the actual awaited service calls in PairAndConfigureWorkflow.RunAsync -
/// ApplyingConfiguration covers both connecting and configuring (the SDK does both within one
/// ApplyAsync call, with no observable seam between them), and TestingConnection covers both
/// waiting out the reboot and the final SGD confirmation (same reason, within TestConnectionAsync).
/// </summary>
public enum PairingWorkflowState
{
    NotStarted,
    ApplyingConfiguration,
    Restarting,
    TestingConnection,
    Succeeded,
    Failed,
}
