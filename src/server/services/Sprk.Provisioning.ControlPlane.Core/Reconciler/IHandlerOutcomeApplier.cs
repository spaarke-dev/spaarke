// -----------------------------------------------------------------------------
// IHandlerOutcomeApplier.cs
//
// L2 CONTROL-PLANE handler-outcome-application seam (task 104, Phase C''
// Wave G-1 -- extracted from StateReconcilerService.ApplyHandlerOutcomeAsync
// per design-study-ds2-dispatcher-design.md §5, closing gap C2.1).
//
// SPEC / DESIGN references:
//   - spec.md FR-24:      4-class §4C failure taxonomy; Quarantined runs
//                         BLOCK new runs against the same customerId until
//                         the operator clears.
//   - design.md §4.2b:    "The dispatcher does NOT advance the DAG -- it
//                         applies outcomes, and the reconciler's existing 5s
//                         tick owns advancement." This interface is the
//                         dispatcher's (task 102) step-6 consumption point.
//   - DS-2 §5:            ApplyHandlerOutcomeAsync exists "precisely as the
//                         dispatcher's hook" (StateReconcilerService.cs
//                         header, pre-extraction) -- the wiring hook lives
//                         here so the §4C classifier + transition table
//                         remain the SINGLE source of truth for any consumer
//                         (StateReconcilerService's own internal shim,
//                         in-process tests, and the dispatcher).
//
// WHY THIS SEAM EXISTS (CLAUDE.md §11 component justification):
//   Existing    -- the pre-extraction ApplyHandlerOutcomeAsync already
//                  implements the full §4C taxonomy correctly; nothing new
//                  needed designing.
//   Extension   -- the method's own header comment already declared itself
//                  the wiring hook for exactly this purpose; extraction (not
//                  reimplementation) is the correct move.
//   Cost-of-doing-nothing -- without this seam, task 102's dispatcher would
//                  either duplicate the §4C classifier/transition-table logic
//                  (a second source of truth, drift-prone) or have no way to
//                  apply outcomes at all.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// Applies a handler's <see cref="HandlerResult"/> outcome to the owning
/// <see cref="ProvisioningRun"/>'s Cosmos state per the design.md §4C
/// rollback taxonomy. Registered Scoped in <c>ReconcilerModule</c> --
/// resolved per-tick by <see cref="StateReconcilerService"/>'s internal
/// delegating shim and, from task 102 onward, per-message by
/// <c>ProvisioningHandlerDispatcher</c> from its own Service-Bus-message DI
/// scope.
/// </summary>
public interface IHandlerOutcomeApplier
{
    /// <summary>
    /// Applies <paramref name="outcome"/> to <paramref name="run"/>. On
    /// <see cref="HandlerResult.Success"/> this is a no-op (handlers own
    /// their own <c>CompletedPhases</c> append + Cosmos write on success --
    /// see <see cref="StateReconcilerService"/> file header for the "reconciler
    /// does NOT write to Cosmos" invariant, which this applier upholds on the
    /// Success path). On <see cref="HandlerResult.Failure"/>, classifies via
    /// the injected <c>IFailureClassifier</c>, maps to a target
    /// <see cref="RunStatus"/> + re-enqueue + guard-release decision via
    /// <c>RollbackTransitions</c> (the single source of truth -- never
    /// re-implement this mapping in a consumer), persists via
    /// <c>IProvisioningRunRepository.ReplaceRunAsync</c> using optimistic
    /// concurrency, and -- ETag permitting -- auto-re-enqueues on
    /// <c>RetryableWithCleanup</c> with an incremented <c>attempt</c> (task
    /// 107) and releases the I5 same-customer guard on guard-releasing
    /// transitions (task 104 gap closure; today only
    /// <c>FailureClass.SuccessfulButDrifted</c>).
    /// </summary>
    /// <param name="run">Run to update. The document's ETag must be current for the persisted <see cref="IProvisioningRunRepository.ReplaceRunAsync"/> call.</param>
    /// <param name="ifMatchEtag">The ETag returned by the most recent <see cref="IProvisioningRunRepository.ReadRunAsync"/>.</param>
    /// <param name="outcome">Handler outcome to translate into a Cosmos state transition.</param>
    /// <param name="handlerId">Handler identifier for provenance + re-enqueue envelope construction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transition applied (target status + whether a re-enqueue was fired).</returns>
    Task<HandlerOutcomeApplied> ApplyHandlerOutcomeAsync(
        ProvisioningRun run,
        string ifMatchEtag,
        HandlerResult outcome,
        string handlerId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result record for <see cref="IHandlerOutcomeApplier.ApplyHandlerOutcomeAsync"/>
/// -- captures what the applier decided per the §4C rollback taxonomy so
/// callers + Kusto pivots can assert on it. <see cref="FailureClass"/> is
/// null on the Success path (no classification fired). Moved verbatim from
/// the pre-extraction <c>StateReconcilerService.HandlerOutcomeApplied</c>
/// nested record (task 104) -- promoted to a top-level <c>public</c> record
/// so task 102's dispatcher (a different call graph within the same L2
/// solution) can consume it across the interface boundary.
/// </summary>
public sealed record HandlerOutcomeApplied(
    RunStatus TargetStatus,
    bool Reenqueued,
    FailureClass? FailureClass);
