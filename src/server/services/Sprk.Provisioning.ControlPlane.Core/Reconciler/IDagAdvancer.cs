// -----------------------------------------------------------------------------
// IDagAdvancer.cs
//
// L2 CONTROL-PLANE handler-dependency computation seam (task 058, Wave C5).
//
// PURPOSE:
//   Given a <see cref="ProvisioningRun"/> snapshot, compute which handlers
//   are READY to dispatch — i.e. their upstream dependencies (per design.md
//   §4.1 DAG) are all present in <see cref="ProvisioningRun.CompletedPhases"/>
//   AND the handler itself is NOT yet in <see cref="ProvisioningRun.CompletedPhases"/>.
//
//   Pure function — no I/O, no time, no state. Unit-testable in isolation
//   with concrete <see cref="ProvisioningRun"/> instances (no mocks needed).
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 DAG
//     diagram (handler dependencies).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.2
//     (state-reconciler consumer).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// Computes the set of handlers that are ready-to-dispatch for a given run
/// snapshot, per the design.md §4.1 handler dependency DAG. Pure function —
/// consumers depend on <see cref="ProvisioningRun.CompletedPhases"/> as the
/// source of truth for "which handlers are done".
/// </summary>
public interface IDagAdvancer
{
    /// <summary>
    /// Returns handler identifiers whose upstream dependencies are all present
    /// in <see cref="ProvisioningRun.CompletedPhases"/> AND which have not
    /// yet completed themselves. Terminal statuses (<see cref="RunStatus.Completed"/>,
    /// <see cref="RunStatus.Failed"/>, <see cref="RunStatus.Cancelled"/>,
    /// <see cref="RunStatus.Quarantined"/>) return an empty list — the run
    /// no longer participates in DAG advancement.
    /// </summary>
    /// <remarks>
    /// Entry-point handlers (H0 preflight + H0.5 Model 2 consent-capture) are
    /// dispatched by the endpoint layer / BFF, NEVER by the reconciler.
    /// The implementation excludes them from the ready set even when their
    /// (empty) dependency requirement would otherwise mark them ready.
    /// </remarks>
    /// <param name="run">Run snapshot to evaluate. Non-null.</param>
    /// <returns>
    /// Ordered list of handler ids that are ready-to-dispatch. Order is
    /// deterministic (sorted) but semantically all returned handlers are
    /// parallel-safe — the reconciler enqueues each independently.
    /// </returns>
    IReadOnlyList<string> ComputeReadyHandlers(ProvisioningRun run);
}
