// -----------------------------------------------------------------------------
// RollbackTransitions.cs
//
// L2 CONTROL-PLANE §4C rollback semantics — pure-function policy that maps a
// <see cref="Sprk.Provisioning.ControlPlane.Handlers.FailureClass"/> to the
// downstream state-transition tuple (target <see cref="RunStatus"/> +
// re-enqueue flag + customer-guard-release flag) per design.md §4C.
//
// SPEC / DESIGN references:
//   - spec.md FR-24:  4-class failure taxonomy + operator-facing clear-
//                     quarantine endpoint; Quarantined runs BLOCK new runs
//                     against the same customerId until cleared.
//   - design.md §4C:  4-class taxonomy + Cosmos state-transition table:
//                       Running -> Failed         (handler threw, retryable)
//                       Running -> Quarantined    (un-recoverable partial state)
//                       Failed  -> Running        (operator called resume_run)
//                       Quarantined -> Cancelled  (operator explicitly abandons)
//                       Cancelled -> (clears sprk_currentrunid on registry)
//
// FABLE M-9 ALIGNMENT (per POML guidance):
//   The 4 classes are ENUM. Every switch expression over
//   <see cref="Sprk.Provisioning.ControlPlane.Handlers.FailureClass"/> in this
//   file uses C# switch-expressions with a discard `_ => throw
//   UnreachableException` guard so a new class added to the enum WITHOUT a
//   corresponding branch here FAILS THE BUILD at the exhaustiveness check
//   (compiler warning CS8524 escalates to error via TreatWarningsAsErrors
//   inherited from Directory.Build.props). This is the "exhaustive switch"
//   pattern — silent misclassification is a compile-time impossibility.
//
// PURITY:
//   All methods are static + pure — no I/O, no time, no state. Trivially unit-
//   testable with concrete enum values.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <summary>
/// Pure-function policy encoding the design.md §4C state-transition table.
/// Consumers (handlers, reconciler, tests) call the static methods to derive
/// the target Cosmos state + re-dispatch decision from a
/// <see cref="FailureClass"/> — never re-implement the mapping in place.
/// </summary>
public static class RollbackTransitions
{
    /// <summary>
    /// Maps a <see cref="FailureClass"/> to its terminal-or-continuation
    /// <see cref="RunStatus"/> per design.md §4C row:
    /// <list type="bullet">
    ///   <item><see cref="FailureClass.Resumable"/>            -> <see cref="RunStatus.Failed"/>       (operator resolves external precondition; POST /api/runs/{id}/resume re-drives).</item>
    ///   <item><see cref="FailureClass.RetryableWithCleanup"/> -> <see cref="RunStatus.Failed"/>       (handler is re-dispatched with same handler-level idempotency key; own idempotency handles partial write. Task 107: the Service Bus envelope's Attempt is incremented so its MessageId differs from the original.)</item>
    ///   <item><see cref="FailureClass.QuarantineRequired"/>   -> <see cref="RunStatus.Quarantined"/>  (blocks new runs against same customerId until clear-quarantine).</item>
    ///   <item><see cref="FailureClass.SuccessfulButDrifted"/> -> <see cref="RunStatus.Completed"/>    (H13 acceptance detected drift; operator re-runs affected phases via resumeFromPhase).</item>
    /// </list>
    /// </summary>
    /// <param name="failureClass">§4C failure classification.</param>
    /// <returns>Target <see cref="RunStatus"/> the run's document should transition to.</returns>
    /// <exception cref="UnreachableException">Thrown at runtime if a new FailureClass is added without a corresponding branch here — compile-time CS8524 warning-as-error is the primary guard.</exception>
    public static RunStatus MapToRunStatus(FailureClass failureClass) =>
        failureClass switch
        {
            FailureClass.Resumable => RunStatus.Failed,
            FailureClass.RetryableWithCleanup => RunStatus.Failed,
            FailureClass.QuarantineRequired => RunStatus.Quarantined,
            FailureClass.SuccessfulButDrifted => RunStatus.Completed,
            _ => throw new UnreachableException(
                $"FailureClass.{failureClass} not handled in RollbackTransitions.MapToRunStatus — " +
                "add a branch here + a design.md §4C row + a test in RollbackTransitionsTests.cs."),
        };

    /// <summary>
    /// Returns true when the reconciler should re-enqueue the failed handler
    /// automatically (§4C RetryableWithCleanup — handler's own idempotency
    /// handles partial write). Resumable requires operator action (POST resume);
    /// QuarantineRequired requires operator action (POST clear-quarantine);
    /// SuccessfulButDrifted requires operator action (POST resume with
    /// resumeFromPhase). Only RetryableWithCleanup is safe to auto-retry.
    /// </summary>
    /// <param name="failureClass">§4C failure classification.</param>
    /// <returns>True when the reconciler should re-dispatch the same handler with the same envelope.</returns>
    /// <exception cref="UnreachableException">Thrown at runtime if a new FailureClass is added without a corresponding branch here.</exception>
    public static bool ShouldReEnqueue(FailureClass failureClass) =>
        failureClass switch
        {
            FailureClass.Resumable => false,
            FailureClass.RetryableWithCleanup => true,
            FailureClass.QuarantineRequired => false,
            FailureClass.SuccessfulButDrifted => false,
            _ => throw new UnreachableException(
                $"FailureClass.{failureClass} not handled in RollbackTransitions.ShouldReEnqueue — " +
                "add a branch here + a design.md §4C row + a test in RollbackTransitionsTests.cs."),
        };

    /// <summary>
    /// Returns true when the run's transition should ALSO release the same-
    /// customer serialization guard (task 059 <c>ICustomerRunGuard.ReleaseAsync</c>).
    /// <see cref="FailureClass.QuarantineRequired"/> keeps the customer BLOCKED
    /// (spec.md FR-24 SCOPE: "Quarantined runs block NEW runs against the same
    /// customerId... a customer with a Quarantined active run cannot start a
    /// new run until cleared") — the guard is released ONLY when clear-
    /// quarantine explicitly runs. All other classes release the guard so a
    /// new run can start.
    /// </summary>
    /// <param name="failureClass">§4C failure classification.</param>
    /// <returns>True when the customer-run-guard (task 059) should be released as part of the transition.</returns>
    /// <exception cref="UnreachableException">Thrown at runtime if a new FailureClass is added without a corresponding branch here.</exception>
    public static bool ShouldReleaseCustomerGuard(FailureClass failureClass) =>
        failureClass switch
        {
            // EXEC-07 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
            // 2026-08-27): Resumable maps to terminal RunStatus.Failed. Before
            // EXEC-07, this held the guard on the failing runId so the operator
            // could POST /resume — but sprk_currentrunid also stayed pinned to
            // the failed runId permanently, so a fresh POST /api/runs 409'd
            // forever until an operator manually PATCHed the registry via pac
            // data. A single Failed run poisoned the customer indefinitely —
            // statistically inevitable during F19/F20 hardening (real-world
            // halt on Session-13's "downstream writes will flow cleanly"
            // promise). EXEC-07 releases the guard on Failed so a fresh run
            // is possible. Trade-off: an operator POST /resume of the OLD
            // runId concurrent with a fresh POST /api/runs for the SAME
            // customer creates dual dispatch (both against the same customer's
            // Azure resources). This is an operator-avoidable race — the
            // resume-path continues to work for the old runId, but the
            // operator is expected to pick ONE recovery path (resume the
            // failed run OR start fresh). Ideally ResumeRun would re-acquire
            // the guard against the resumed runId; that endpoint enhancement
            // is a follow-on row.
            FailureClass.Resumable => true, // EXEC-07 (2026-08-27): unblock fresh runs after Failed.
            FailureClass.RetryableWithCleanup => false, // Auto-retry is in-flight; keep guard held.
            FailureClass.QuarantineRequired => false, // BLOCKS new runs per spec FR-24 SCOPE.
            FailureClass.SuccessfulButDrifted => true,  // Run is Completed; customer may start a new run (or repair phase).
            _ => throw new UnreachableException(
                $"FailureClass.{failureClass} not handled in RollbackTransitions.ShouldReleaseCustomerGuard — " +
                "add a branch here + a design.md §4C row + a test in RollbackTransitionsTests.cs."),
        };
}
