// -----------------------------------------------------------------------------
// ICustomerRunGuard.cs
//
// L2 CONTROL-PLANE per-customer serialization contract (task 059, Wave C5).
//
// SPEC / DESIGN references:
//   - spec.md FR-23:  Same-customer runs are serialized via optimistic upsert
//                     of `sprk_dataverseenvironment.sprk_currentrunid`
//                     (null -> newRunId); conflict = 409 with winning run ID;
//                     cross-customer runs execute in parallel.
//   - spec.md §4D I5: One of the 5 binding tenant-isolation invariants.
//                     Verified by the ArchTest suite (task 064).
//   - design.md §4.2 (Concurrency — I5 resolved v3): "L2 attempts
//                     sprk_currentrunid = null -> newRunId conditionally;
//                     conflict -> 409 to caller with the winning run ID."
//   - design.md §4C:  On terminal transitions (Completed / Failed / Cancelled)
//                     the column is cleared to null. Quarantined runs KEEP
//                     the guard set until the operator explicitly clears
//                     (task 061 owns the Quarantined semantics + clear-quarantine
//                     release path).
//
// ADR references:
//   - ADR-044:        sprk_currentrunid stores canonical GUID form (bare,
//                     lowercase). Callers passing a Xrm-sourced GUID MUST
//                     normalize (e.g., cleanGuid on the client) — the guard
//                     itself normalizes at its own ingress to be defensive.
//   - ADR-032:        Registered UNCONDITIONALLY (no feature-gate DI branch).
//                     Test-only in-memory doubles replace the interface at DI
//                     level in the L2 test project.
//   - ADR-004:        The guard is orchestration infrastructure (Path A
//                     exception at L2 scope per spec.md ADR Tensions row 1)
//                     — NOT itself an IJobHandler.
//
// IDEMPOTENCY (project MUST rule):
//   TryAcquireAsync is idempotent for the SAME (customerId, runId) tuple —
//   a re-call with the same runId returns <see cref="AcquireResult.Success"/>
//   even when the column already holds that runId. This is the crash-recovery
//   contract: a resumed run must be able to re-acquire its own guard without
//   racing itself out.
//
// PROTECTION (project MUST rule):
//   ReleaseAsync clears the column ONLY IF its current value matches the
//   caller's runId. A stale caller (e.g., a delayed cancel-completion
//   handler for a run whose guard has already been re-acquired by a fresh
//   run) MUST NOT clear a different run's grab.
//
// PRIMARY GUARD IS DURABLE:
//   Per project constraint (see POML), the guard MUST NOT be Redis or in-memory
//   as the source of truth — it MUST survive L2 App Service restarts. Dataverse
//   `sprk_dataverseenvironment.sprk_currentrunid` is the single source of truth;
//   the guard is a thin optimistic-concurrency wrapper over that column.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Concurrency;

/// <summary>
/// Same-customer run serialization guard. Implements the I5 concurrency
/// invariant (spec.md §4D + FR-23) by owning optimistic-concurrency writes to
/// <c>sprk_dataverseenvironment.sprk_currentrunid</c>.
/// </summary>
public interface ICustomerRunGuard
{
    /// <summary>
    /// Attempts to atomically claim <paramref name="customerId"/>'s registry
    /// row for the given <paramref name="runId"/>. Semantics:
    /// <list type="bullet">
    ///   <item><description>column is null -> set to <paramref name="runId"/>
    ///     via If-Match ETag guard -> <see cref="AcquireResult.Success"/>.</description></item>
    ///   <item><description>column equals <paramref name="runId"/> already ->
    ///     <see cref="AcquireResult.Success"/> (idempotent re-acquire).</description></item>
    ///   <item><description>column holds a DIFFERENT non-null value ->
    ///     <see cref="AcquireResult.Conflict"/> with the winning run id.
    ///     No write is attempted.</description></item>
    ///   <item><description>ETag race collides on the PATCH attempt ->
    ///     the winning run's value is re-read and returned as
    ///     <see cref="AcquireResult.Conflict"/> (may be the same as our runId
    ///     if we lost a race to ourselves, in which case Success wins).</description></item>
    ///   <item><description>registry row not found for
    ///     <paramref name="customerId"/> -> <see cref="AcquireResult.TransientFailure"/>
    ///     with a diagnostic (the caller should surface as 502 not 409 — a
    ///     missing registry row is a data-integrity issue, not a concurrency
    ///     conflict).</description></item>
    ///   <item><description>transport / auth failure -> <see cref="AcquireResult.TransientFailure"/>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="customerId">Customer short-id (matches <c>sprk_dataverseenvironment.sprk_customerid</c>).</param>
    /// <param name="runId">The run identifier to claim; canonical GUID string per ADR-044.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AcquireResult> TryAcquireAsync(
        string customerId,
        string runId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases the guard by clearing <c>sprk_currentrunid</c> back to null,
    /// but ONLY IF the column currently holds <paramref name="runId"/>. If the
    /// column holds a different value (or null) the release is a documented
    /// no-op (<see cref="ReleaseResult.Mismatched"/>) — this protects against
    /// a stale terminal-state handler clearing a fresh run's guard.
    /// </summary>
    /// <param name="customerId">Customer short-id.</param>
    /// <param name="runId">The run identifier this caller believes it holds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ReleaseResult> ReleaseAsync(
        string customerId,
        string runId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of <see cref="ICustomerRunGuard.TryAcquireAsync"/> — a discriminated
/// union covering the three meaningful outcomes: success, contention with a
/// different run, or a transient failure the caller should surface to the
/// operator.
/// </summary>
public abstract record AcquireResult
{
    private AcquireResult() { }

    /// <summary>The guard is held by <paramref name="RunId"/> for <paramref name="CustomerId"/>.</summary>
    public sealed record Success(string CustomerId, string RunId) : AcquireResult;

    /// <summary>
    /// A different run currently holds the guard for <paramref name="CustomerId"/>.
    /// The caller SHOULD surface HTTP 409 Conflict with the payload
    /// <c>{ "winningRunId": WinningRunId, "reasonCode": ReasonCode }</c> per
    /// spec.md FR-23 acceptance.
    /// </summary>
    /// <param name="ReasonCode">
    /// A stable machine-readable reason code. Defaults to
    /// <see cref="AcquireConflictReasonCodes.AlreadyInFlight"/>. Task 061
    /// (§4C rollback + Quarantined) will introduce
    /// <see cref="AcquireConflictReasonCodes.Quarantined"/> so the L2 endpoint
    /// can differentiate "wait for the in-flight run" from "operator must clear
    /// the quarantine first" without inspecting Cosmos.
    /// </param>
    public sealed record Conflict(string CustomerId, string WinningRunId, string ReasonCode) : AcquireResult;

    /// <summary>
    /// The guard could not be evaluated (transport error, missing registry
    /// row, or unexpected Dataverse response). The caller SHOULD surface HTTP
    /// 502 with the diagnostic — this is NOT a routine 409 conflict.
    /// </summary>
    public sealed record TransientFailure(string CustomerId, string RunId, string Diagnostic) : AcquireResult;
}

/// <summary>
/// Result of <see cref="ICustomerRunGuard.ReleaseAsync"/>.
/// </summary>
public abstract record ReleaseResult
{
    private ReleaseResult() { }

    /// <summary>The guard was cleared; the column is now null.</summary>
    public sealed record Released(string CustomerId, string RunId) : ReleaseResult;

    /// <summary>
    /// The guard's current value did not match <paramref name="RunId"/>; the
    /// column was NOT changed. <paramref name="CurrentValue"/> is the value we
    /// read (may be null if the column was already cleared, or a different
    /// runId if we lost a race).
    /// </summary>
    public sealed record Mismatched(string CustomerId, string RunId, string? CurrentValue) : ReleaseResult;

    /// <summary>
    /// The registry row for <paramref name="CustomerId"/> could not be found.
    /// A missing registry row means release is a no-op (nothing to clear).
    /// </summary>
    public sealed record NotFound(string CustomerId, string RunId) : ReleaseResult;

    /// <summary>Transport / auth / unexpected-response failure.</summary>
    public sealed record TransientFailure(string CustomerId, string RunId, string Diagnostic) : ReleaseResult;
}

/// <summary>
/// Stable machine-readable reason codes for <see cref="AcquireResult.Conflict"/>.
/// The endpoint layer surfaces these unchanged in the 409 body per spec.md
/// FR-23 acceptance.
/// </summary>
public static class AcquireConflictReasonCodes
{
    /// <summary>
    /// A different run for the same customer is currently in flight (Running
    /// or WaitingOnGate). The caller should wait or cancel the in-flight run.
    /// </summary>
    public const string AlreadyInFlight = "AlreadyInFlight";

    /// <summary>
    /// The current run is Quarantined per design.md §4C — <c>sprk_currentrunid</c>
    /// intentionally remains set to block new runs until the operator invokes
    /// <c>POST /api/runs/{id}/clear-quarantine</c>. When the winning run's
    /// Cosmos status is Quarantined, TryAcquireAsync returns this reason code
    /// so the operator sees a distinct signal from a routine "already in flight"
    /// (integration with task 061 rollback semantics).
    /// </summary>
    public const string Quarantined = "Quarantined";
}
