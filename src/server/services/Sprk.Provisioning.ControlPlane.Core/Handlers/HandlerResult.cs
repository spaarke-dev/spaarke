// -----------------------------------------------------------------------------
// HandlerResult.cs
//
// Discriminated result of <see cref="IProvisioningHandler.HandleAsync"/>. Encodes
// success (with idempotency key) OR failure classified per the §4C rollback
// taxonomy — never throws on domain failures.
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §4C — 4-class
//     rollback taxonomy: Resumable / Retryable-with-cleanup / Quarantine-required
//     / Successful-but-drifted. H0 preflight is quintessentially Resumable
//     (external precondition — quota bump / cert bootstrap — resolves the
//     issue; operator then POST /api/runs/{id}/resume).
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-24 (rollback
//     semantics) + FR-01 (H0 acceptance: block run + emit distinct diagnostic
//     per failing check).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>
/// Discriminated outcome of an <see cref="IProvisioningHandler"/> invocation.
/// Exhaustive: <see cref="Success"/> | <see cref="Failure"/>. Callers pattern-
/// match to decide the Cosmos state transition + the operator-facing HTTP
/// status the endpoint layer (wave C5) surfaces.
/// </summary>
public abstract record HandlerResult
{
    private HandlerResult() { }

    /// <summary>
    /// The handler completed its work successfully OR the idempotency check
    /// short-circuited a duplicate invocation (both are Success — the
    /// reconciler cannot tell them apart, by design).
    /// </summary>
    /// <param name="IdempotencyKey">
    /// The deterministic idempotency key the handler used (e.g.
    /// <c>preflight-{customerId}-{paramHash}</c> for H0). Recorded in
    /// <see cref="Sprk.Provisioning.ControlPlane.Models.CompletedPhase.IdempotencyKey"/>
    /// so subsequent invocations detect the duplicate at the Cosmos level.
    /// </param>
    public sealed record Success(string IdempotencyKey) : HandlerResult;

    /// <summary>
    /// The handler failed. <paramref name="Class"/> tells the reconciler how
    /// to categorize the failure per design.md §4C; <paramref name="RejectionCode"/>
    /// is a machine-stable identifier for the specific failure mode (e.g.
    /// <c>quota-openai-tpm</c>); <paramref name="Diagnostic"/> is the
    /// operator-facing human-readable message.
    /// </summary>
    /// <param name="Class">§4C rollback class (see <see cref="FailureClass"/>).</param>
    /// <param name="RejectionCode">Stable machine-parseable code (e.g. <c>quota-openai-tpm</c>, <c>missing-tenant-id</c>).</param>
    /// <param name="Diagnostic">Human-readable diagnostic for the operator — MUST cite BOTH observed state AND requested capacity where applicable so the operator can file a quota-bump request without guesswork.</param>
    public sealed record Failure(
        FailureClass Class,
        string RejectionCode,
        string Diagnostic) : HandlerResult;
}

/// <summary>
/// The four failure classes from design.md §4C. Determines how the reconciler
/// (wave C5) transitions the ProvisioningRun state + what operator action is
/// required.
/// </summary>
public enum FailureClass
{
    /// <summary>
    /// Handler failed before writing any external side effect (or wrote to
    /// Cosmos only). Operator resolves the external precondition (e.g. quota
    /// bump); <c>POST /api/runs/{id}/resume</c> restarts the failed handler.
    /// H0 preflight failures are quintessentially Resumable.
    /// </summary>
    Resumable = 1,

    /// <summary>
    /// Handler wrote some external state but the write is safe to re-execute
    /// (own idempotency handles partial write). Reconciler re-dispatches the
    /// same handler with the same envelope.
    /// </summary>
    RetryableWithCleanup = 2,

    /// <summary>
    /// Handler wrote un-recoverable partial external state. Cosmos run
    /// transitions to <see cref="Models.RunStatus.Quarantined"/>; blocks new
    /// runs against the same customerId until an operator explicitly clears
    /// via <c>POST /api/runs/{id}/clear-quarantine</c> (spec.md FR-24).
    /// </summary>
    QuarantineRequired = 3,

    /// <summary>
    /// H13 acceptance sample detected drift between deployed state and
    /// intended state despite no handler having failed. Operator re-runs
    /// affected phases with <c>resumeFromPhase</c>. Not applicable to H0.
    /// </summary>
    SuccessfulButDrifted = 4,
}
